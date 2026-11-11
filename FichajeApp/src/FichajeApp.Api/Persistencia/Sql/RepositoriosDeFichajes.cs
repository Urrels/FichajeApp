using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Persistencia.Sql;

// Tabla Fichajes, vista desde dos lados (Strategy y repositorio): lo que registra la app al fichar (Strategy) y lo que
// corrige o carga a mano un administrador (repositorio).
public class FichajesSqlStorage : BaseSql, IFichajeStorageStrategy
{
    public FichajesSqlStorage(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task GuardarAsync(FichajeDto f)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.Fichajes.Add(new FichajeFila
        {
            NombreCompleto = f.NombreCompleto,
            FechaHora = f.FechaHora,
            Latitud = f.Latitud,
            Longitud = f.Longitud,
            Tipo = f.TipoFichaje,
            Lugar = f.Lugar,
            Motivo = Recortar(f.Motivo, 500),
            Precision = f.Precision.HasValue ? Math.Round(f.Precision.Value) : null,
            Direccion = Recortar(f.Direccion, 500),
            Foto = string.IsNullOrWhiteSpace(f.Foto) ? null : f.Foto
        });
        await db.SaveChangesAsync();
    }

    // Los cargados a mano no se cuentan acá: no tienen ubicación y no cambian lo que toca fichar
    public Task<List<FichajeDto>> ObtenerFichajesAsync() => LeerAsync(async db =>
        (await db.Fichajes.Where(f => !f.EsManual).OrderBy(f => f.FechaHora).ThenBy(f => f.Id).ToListAsync())
            .Select(f => new FichajeDto
            {
                NombreCompleto = f.NombreCompleto,
                FechaHora = f.FechaHora,
                Latitud = f.Latitud,
                Longitud = f.Longitud,
                TipoFichaje = f.Tipo,
                Lugar = f.Lugar,
                Motivo = f.Motivo,
                Precision = f.Precision,
                Direccion = f.Direccion,
                Foto = f.Foto
            }).ToList());

    // Sin fichajes hoy el próximo es "Entrada": se devuelve "Salida" para que ProximoTipoAsync lo invierta.
    // Las SALIDAS cargadas a mano cuentan: si el administrador le agrega la salida a una entrada que quedó abierta,
    // el fichaje tiene que verse cerrado (antes se ignoraban y la jornada seguía "abierta"). Una ENTRADA cargada a mano
    // no abre nada: no cambia lo que le toca fichar a la persona.
    public Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto) => LeerAsync(async db =>
    {
        var hoy = DateTime.Now.Date;
        var manana = hoy.AddDays(1);
        var nombre = nombreCompleto.ToLower();
        var tipo = await db.Fichajes
            .Where(f => (!f.EsManual || f.Tipo == "Salida") && f.NombreCompleto.ToLower() == nombre && f.FechaHora >= hoy && f.FechaHora < manana)
            .OrderByDescending(f => f.FechaHora).ThenByDescending(f => f.Id)
            .Select(f => f.Tipo).FirstOrDefaultAsync();
        return string.IsNullOrEmpty(tipo) ? "Salida" : tipo;
    });

    private static string? Recortar(string? texto, int max)
    {
        texto = texto?.Trim();
        if (string.IsNullOrEmpty(texto)) return null;
        return texto.Length <= max ? texto : texto[..max];
    }
}

public class FichajesSqlRepositorio : BaseSql, IFichajesRepositorio
{
    private readonly IMemoryCache _cache;

    public FichajesSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica, IMemoryCache cache) : base(fabrica) => _cache = cache;

    public Task<List<FichajeRegistrado>> ListarAsync() => LeerAsync(async db =>
        (await db.Fichajes.OrderBy(f => f.Id).ToListAsync()).Select(Mapear).ToList());

    public Task<FichajeRegistrado?> ObtenerAsync(int id) => LeerAsync(async db =>
    {
        var fila = await db.Fichajes.FirstOrDefaultAsync(f => f.Id == id);
        return fila is null ? null : Mapear(fila);
    });

    public async Task CorregirFechaHoraAsync(int id, DateTime nuevaFechaHora, string motivo, string corregidoPor, DateTime ahora)
    {
        string nombre;
        DateTime anterior;
        await using (var db = await Fabrica.CreateDbContextAsync())
        {
            var fila = await db.Fichajes.FirstOrDefaultAsync(f => f.Id == id) ?? throw new InvalidOperationException("El fichaje no existe.");
            nombre = fila.NombreCompleto;
            anterior = fila.FechaHora;
            fila.FechaHora = nuevaFechaHora;
            fila.Corregido = true;
            fila.MotivoCorreccion = $"{motivo.Trim()} [antes: {anterior.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}]";
            fila.CorregidoPor = corregidoPor.Trim();
            fila.FechaCorreccion = ahora;
            await db.SaveChangesAsync();
        }
        // El "último tipo del día" cacheado ya no vale: se mueve la hora de un evento
        CachedFichajeStorage.InvalidarUltimoTipo(_cache, nombre, anterior, nuevaFechaHora);
    }

    public async Task AgregarManualAsync(string empleado, string tipo, DateTime fechaHora, string motivo, string corregidoPor, DateTime ahora)
    {
        await using (var db = await Fabrica.CreateDbContextAsync())
        {
            db.Fichajes.Add(new FichajeFila
            {
                NombreCompleto = empleado.Trim(),
                FechaHora = fechaHora,
                Tipo = tipo,
                Lugar = "Manual",
                Motivo = motivo.Trim(),
                EsManual = true,
                Corregido = true,
                MotivoCorreccion = motivo.Trim(),
                CorregidoPor = corregidoPor.Trim(),
                FechaCorreccion = ahora
            });
            await db.SaveChangesAsync();
        }
        CachedFichajeStorage.InvalidarUltimoTipo(_cache, empleado, fechaHora);
    }

    private static FichajeRegistrado Mapear(FichajeFila f) => new()
    {
        Id = f.Id, NombreCompleto = f.NombreCompleto, FechaHora = f.FechaHora, Tipo = f.Tipo, Lugar = f.Lugar,
        Corregido = f.Corregido, MotivoCorreccion = string.IsNullOrWhiteSpace(f.MotivoCorreccion) ? null : f.MotivoCorreccion
    };
}

// Tabla Sitios: los lugares conocidos donde se puede fichar
public class SitiosSqlRepositorio : BaseSql, ISitiosRepository, ISitiosAdminRepositorio
{
    public SitiosSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync() => LeerAsync<IReadOnlyList<Sitio>>(async db =>
        (await db.Sitios.OrderBy(s => s.Id).ToListAsync())
            .Select(s => new Sitio(s.Nombre, s.Latitud, s.Longitud, s.RadioMetros)).ToList());

    public Task<List<SitioAdmin>> ListarAsync() => LeerAsync(async db =>
        (await db.Sitios.OrderBy(s => s.Nombre).ToListAsync())
            .Select(s => new SitioAdmin(s.Id, s.Nombre, s.Latitud, s.Longitud, s.RadioMetros)).ToList());

    public async Task<int> CrearAsync(Sitio sitio)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = new SitioFila { Nombre = sitio.Nombre, Latitud = sitio.Latitud, Longitud = sitio.Longitud, RadioMetros = sitio.RadioMetros };
        db.Sitios.Add(fila);
        await db.SaveChangesAsync();
        return fila.Id;
    }

    public async Task ActualizarAsync(int id, Sitio sitio)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = await db.Sitios.FirstOrDefaultAsync(s => s.Id == id) ?? throw new InvalidOperationException("El lugar no existe.");
        fila.Nombre = sitio.Nombre;
        fila.Latitud = sitio.Latitud;
        fila.Longitud = sitio.Longitud;
        fila.RadioMetros = sitio.RadioMetros;
        await db.SaveChangesAsync();
    }

    public async Task EliminarAsync(int id)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var borradas = await db.Sitios.Where(s => s.Id == id).ExecuteDeleteAsync();
        if (borradas == 0) throw new InvalidOperationException("El lugar no existe.");
    }
}
