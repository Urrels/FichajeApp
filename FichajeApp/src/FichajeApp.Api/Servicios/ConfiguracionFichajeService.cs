using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Configuración de fichaje por cliente (ubicación y foto), editable por el administrador desde la app.
// Se cachea unos segundos porque cada fichaje la consulta.
public class ConfiguracionFichajeService
{
    private const string ClaveCache = "config-fichaje";
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private readonly IConfiguracionFichajeRepositorio _repositorio;
    private readonly BitacoraService _bitacora;
    private readonly IMemoryCache _cache;
    private readonly FichajeOptions _opciones;

    public ConfiguracionFichajeService(IConfiguracionFichajeRepositorio repositorio, BitacoraService bitacora,
        IMemoryCache cache, IOptions<FichajeOptions> opciones)
    {
        _repositorio = repositorio;
        _bitacora = bitacora;
        _cache = cache;
        _opciones = opciones.Value;
    }

    public async Task<ConfiguracionFichaje> ObtenerAsync() =>
        (await _cache.GetOrCreateAsync(ClaveCache, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vida;
            return await _repositorio.ObtenerAsync() ?? new ConfiguracionFichaje
            {
                // Lo de appsettings (Fichaje:ModoUbicacion) sigue valiendo mientras nadie lo cambie desde la app
                Ubicacion = _opciones.ModoLibre ? ConfiguracionFichaje.UbicacionLibre : ConfiguracionFichaje.UbicacionSedeFija,
                Foto = ConfiguracionFichaje.FotoNo
            };
        }))!;

    public async Task GuardarAsync(string actor, GuardarConfiguracionFichajeRequest r)
    {
        var ubicacion = Elegir(r.Ubicacion, ConfiguracionFichaje.Ubicaciones, "ubicación");
        var foto = Elegir(r.Foto, ConfiguracionFichaje.Fotos, "foto");

        await _repositorio.GuardarAsync(new ConfiguracionFichaje { Ubicacion = ubicacion, Foto = foto });
        _cache.Remove(ClaveCache);
        await _bitacora.RegistrarAsync(actor, $"CONFIG_FICHAJE:{ubicacion}/{foto}");
    }

    private static string Elegir(string? valor, string[] permitidos, string cual) =>
        permitidos.FirstOrDefault(p => p.Equals((valor ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"La opción de {cual} no es válida. Opciones: {string.Join(", ", permitidos)}.");
}
