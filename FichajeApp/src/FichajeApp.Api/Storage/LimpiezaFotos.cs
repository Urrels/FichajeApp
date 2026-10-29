using Microsoft.Extensions.Options;
using FichajeApp.Api.Options;

namespace FichajeApp.Api.Storage;

// Borra las fotos que pasaron el tiempo de retención (Fichaje:FotosRetencionDias, 90 por defecto; 0 = no borrar).
// La lógica está separada del servicio en segundo plano para poder probarla con un reloj falso.
public class LimpiezaFotos
{
    private readonly IAlmacenFotos _fotos;
    private readonly TimeProvider _tiempo;
    private readonly int _retencionDias;

    public LimpiezaFotos(IAlmacenFotos fotos, TimeProvider tiempo, IOptions<FichajeOptions> opciones)
    {
        _fotos = fotos;
        _tiempo = tiempo;
        _retencionDias = opciones.Value.FotosRetencionDias;
    }

    // Se conservan los últimos N días completos: con 90, hoy se borra lo anterior al día de hace 90 días
    public Task<int> EjecutarAsync()
    {
        if (_retencionDias <= 0) return Task.FromResult(0);
        return _fotos.EliminarAnterioresAsync(_tiempo.GetLocalNow().DateTime.Date.AddDays(-_retencionDias));
    }
}

// Al arrancar y después una vez por día
public class LimpiezaFotosHostedService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    private readonly LimpiezaFotos _limpieza;
    private readonly ILogger<LimpiezaFotosHostedService> _log;

    public LimpiezaFotosHostedService(LimpiezaFotos limpieza, ILogger<LimpiezaFotosHostedService> log)
    {
        _limpieza = limpieza;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        using var reloj = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                var borradas = await _limpieza.EjecutarAsync();
                if (borradas > 0) _log.LogInformation("Se borraron {Cantidad} fotos de fichajes vencidas.", borradas);
            }
            catch (Exception ex)
            {
                // Un error de disco no tiene que tirar la API
                _log.LogError(ex, "No se pudieron borrar las fotos vencidas");
            }
        }
        while (await reloj.WaitForNextTickAsync(cancelar));
    }
}
