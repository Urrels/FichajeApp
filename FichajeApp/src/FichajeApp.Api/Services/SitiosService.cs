using FichajeApp.Api.Helpers;

namespace FichajeApp.Api.Services;

public class SitiosService : ISitiosService
{
    private readonly ISitiosRepository _repositorio;

    public SitiosService(ISitiosRepository repositorio)
    {
        _repositorio = repositorio;
    }

    // Devuelve el nombre del sitio más cercano que contiene la coordenada, o null
    public async Task<string?> BuscarSitioAsync(double lat, double lon)
    {
        string? mejor = null;
        var mejorDistancia = double.MaxValue;

        foreach (var sitio in await _repositorio.ObtenerSitiosAsync())
        {
            var distancia = GeoValidator.DistanciaMetros(lat, lon, sitio.Latitud, sitio.Longitud);
            if (distancia <= sitio.RadioMetros && distancia < mejorDistancia)
            {
                mejorDistancia = distancia;
                mejor = sitio.Nombre;
            }
        }

        return mejor;
    }
}
