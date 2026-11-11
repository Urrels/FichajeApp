using FichajeApp.Api.Models;
using FichajeApp.Api.Persistencia;
using FichajeApp.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FichajeApp.Api.Servicios;

// Un punto del recorrido de una persona durante su jornada
public class PuntoRecorrido
{
    public DateTimeOffset FechaHora { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
}

public class PuntoGuardado
{
    public DateTime FechaHora { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
}

public interface IRecorridoRepositorio
{
    Task AgregarAsync(int empleadoId, IReadOnlyCollection<PuntoGuardado> puntos);
    Task<List<PuntoGuardado>> ListarAsync(int empleadoId, DateTime desde, DateTime hasta);
    Task<int> EliminarAnterioresAsync(DateTime antesDe);
}

public class RecorridoSqlRepositorio : Persistencia.Sql.BaseSql, IRecorridoRepositorio
{
    public RecorridoSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task AgregarAsync(int empleadoId, IReadOnlyCollection<PuntoGuardado> puntos)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.RecorridoPuntos.AddRange(puntos.Select(p => new RecorridoPuntoFila
        {
            EmpleadoId = empleadoId, FechaHora = p.FechaHora, Latitud = p.Latitud, Longitud = p.Longitud, Precision = p.Precision
        }));
        await db.SaveChangesAsync();
    }

    public Task<List<PuntoGuardado>> ListarAsync(int empleadoId, DateTime desde, DateTime hasta) => LeerAsync(async db =>
        (await db.RecorridoPuntos.Where(p => p.EmpleadoId == empleadoId && p.FechaHora >= desde && p.FechaHora < hasta)
            .OrderBy(p => p.FechaHora).ToListAsync())
        .Select(p => new PuntoGuardado { FechaHora = p.FechaHora, Latitud = p.Latitud, Longitud = p.Longitud, Precision = p.Precision })
        .ToList());

    public async Task<int> EliminarAnterioresAsync(DateTime antesDe)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        return await db.RecorridoPuntos.Where(p => p.FechaHora < antesDe).ExecuteDeleteAsync();
    }
}

public record ResultadoRecorrido(int Aceptados, bool Seguir, string? Motivo = null);

// Recorrido durante la jornada: el teléfono manda puntos mientras hay una jornada abierta y el cliente lo activó.
// El servidor lo hace valer: sin la opción activada o sin jornada abierta no guarda nada y le dice al teléfono que pare.
public class RecorridoService
{
    public const int MaxPuntosPorEnvio = 100;
    public const int RetencionDias = 90;
    private const double PrecisionMaxima = 200;

    private readonly IRecorridoRepositorio _repo;
    private readonly IFichajeService _fichaje;
    private readonly TimeProvider _tiempo;

    public RecorridoService(IRecorridoRepositorio repo, IFichajeService fichaje, TimeProvider tiempo)
    {
        _repo = repo;
        _fichaje = fichaje;
        _tiempo = tiempo;
    }

    public async Task<ResultadoRecorrido> RegistrarAsync(EmpleadoDto empleado, IReadOnlyList<PuntoRecorrido>? puntos)
    {
        var config = await _fichaje.ConfiguracionAsync();
        if (!config.SeguimientoRecorrido)
            return new ResultadoRecorrido(0, false, "Tu empresa no tiene activado el registro del recorrido.");

        // Solo mientras la jornada está abierta (la última marca es una Entrada)
        if (await _fichaje.ProximoTipoAsync(empleado.NombreCompleto) != "Salida")
            return new ResultadoRecorrido(0, false, "No hay una jornada abierta.");

        var ahora = _tiempo.GetLocalNow().DateTime;
        var validos = (puntos ?? new List<PuntoRecorrido>())
            .Take(MaxPuntosPorEnvio)
            .Select(p => new PuntoGuardado
            {
                FechaHora = TimeZoneInfo.ConvertTime(p.FechaHora, _tiempo.LocalTimeZone).DateTime,
                Latitud = p.Latitud, Longitud = p.Longitud, Precision = p.Precision
            })
            .Where(p => Coordenadas(p) && (p.Precision is null || p.Precision <= PrecisionMaxima)
                        && p.FechaHora >= ahora.AddHours(-24) && p.FechaHora <= ahora.AddMinutes(5))
            .ToList();

        if (validos.Count > 0) await _repo.AgregarAsync(empleado.Id, validos);
        return new ResultadoRecorrido(validos.Count, true);
    }

    private static bool Coordenadas(PuntoGuardado p) =>
        double.IsFinite(p.Latitud) && double.IsFinite(p.Longitud) &&
        p.Latitud is >= -90 and <= 90 && p.Longitud is >= -180 and <= 180 &&
        !(p.Latitud == 0 && p.Longitud == 0);

    // Los puntos de un día (de 00:00 a 24:00), para el supervisor
    public Task<List<PuntoGuardado>> ObtenerAsync(int empleadoId, DateTime dia) =>
        _repo.ListarAsync(empleadoId, dia.Date, dia.Date.AddDays(1));

}

// Al arrancar y después una vez por día: el recorrido se guarda 90 días
public class LimpiezaRecorridoHostedService : BackgroundService
{
    private readonly IRecorridoRepositorio _repo;
    private readonly TimeProvider _tiempo;
    private readonly ILogger<LimpiezaRecorridoHostedService> _log;

    public LimpiezaRecorridoHostedService(IRecorridoRepositorio repo, TimeProvider tiempo, ILogger<LimpiezaRecorridoHostedService> log)
    {
        _repo = repo;
        _tiempo = tiempo;
        _log = log;
    }

    public Task<int> LimpiarAsync() =>
        _repo.EliminarAnterioresAsync(_tiempo.GetLocalNow().DateTime.Date.AddDays(-RecorridoService.RetencionDias));

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        using var reloj = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try { var n = await LimpiarAsync(); if (n > 0) _log.LogInformation("Recorrido: se borraron {N} puntos viejos", n); }
            catch (Exception ex) { _log.LogWarning("No se pudo limpiar el recorrido: {Tipo}", ex.GetType().Name); }
        }
        while (await reloj.WaitForNextTickAsync(cancelar));
    }
}
