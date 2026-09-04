using FichajeApp.Api.Models;

namespace FichajeApp.Api.Observers;

public class LogFichajeObserver : IFichajeObserver
{
    private readonly ILogger<LogFichajeObserver> _logger;

    public LogFichajeObserver(ILogger<LogFichajeObserver> logger)
    {
        _logger = logger;
    }

    public void OnFichajeRegistrado(FichajeDto fichaje)
    {
        _logger.LogInformation(
            "Fichaje registrado: {Nombre} - {Tipo} - {Fecha} - ({Lat}, {Lon})",
            fichaje.NombreCompleto, fichaje.TipoFichaje, fichaje.FechaHora, fichaje.Latitud, fichaje.Longitud);
    }
}
