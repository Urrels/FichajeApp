using Microsoft.EntityFrameworkCore;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Persistencia.Sql;

// Tabla Apariencia (una sola fila): nombre, colores y logo que el administrador define desde la app
public class AparienciaSqlRepositorio : BaseSql, IAparienciaRepositorio
{
    public AparienciaSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<AparienciaGuardada> ObtenerAsync() => LeerAsync(async db =>
    {
        var f = await db.Apariencia.FirstOrDefaultAsync(a => a.Id == 1);
        return f is null
            ? new AparienciaGuardada()
            : new AparienciaGuardada
            {
                Nombre = Vacio(f.Nombre), ColorPrimario = Vacio(f.ColorPrimario), ColorPrimarioTexto = Vacio(f.ColorPrimarioTexto),
                ColorAcento = Vacio(f.ColorAcento), Logo = f.Logo is { Length: > 0 } ? f.Logo : null
            };
    });

    // Reemplaza todo: lo que viene vacío queda vacío ("usar el del cliente")
    public async Task GuardarAsync(AparienciaGuardada a)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var f = await db.Apariencia.FirstOrDefaultAsync(x => x.Id == 1);
        if (f is null) db.Apariencia.Add(f = new AparienciaFila { Id = 1 });
        f.Nombre = Vacio(a.Nombre);
        f.ColorPrimario = Vacio(a.ColorPrimario);
        f.ColorPrimarioTexto = Vacio(a.ColorPrimarioTexto);
        f.ColorAcento = Vacio(a.ColorAcento);
        f.Logo = a.Logo is { Length: > 0 } ? a.Logo : null;
        await db.SaveChangesAsync();
    }

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}

// Tabla ConfigFichaje (una sola fila): cómo se ficha en este cliente. Sin fila = todavía no se guardó nada.
public class ConfiguracionFichajeSqlRepositorio : BaseSql, IConfiguracionFichajeRepositorio
{
    public ConfiguracionFichajeSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<ConfiguracionFichaje?> ObtenerAsync() => LeerAsync(async db =>
    {
        var f = await db.ConfigFichaje.FirstOrDefaultAsync(c => c.Id == 1);
        return f is null
            ? null
            : new ConfiguracionFichaje { Ubicacion = f.Ubicacion, Foto = f.Foto, Modo = f.Modo, PrecisionMaxMetros = f.PrecisionMaxMetros, SeguimientoRecorrido = f.SeguimientoRecorrido };
    });

    public async Task GuardarAsync(ConfiguracionFichaje c)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var f = await db.ConfigFichaje.FirstOrDefaultAsync(x => x.Id == 1);
        if (f is null) db.ConfigFichaje.Add(f = new ConfigFichajeFila { Id = 1 });
        f.Ubicacion = c.Ubicacion;
        f.Foto = c.Foto;
        f.Modo = c.Modo;
        f.PrecisionMaxMetros = c.PrecisionMaxMetros;
        f.SeguimientoRecorrido = c.SeguimientoRecorrido;
        await db.SaveChangesAsync();
    }
}
