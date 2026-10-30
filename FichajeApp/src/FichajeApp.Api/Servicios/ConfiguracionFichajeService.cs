using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Configuración de fichaje por cliente (ubicación, foto y modo personal o kiosco), editable por el administrador desde la app.
// Se cachea unos segundos porque cada fichaje la consulta.
public class ConfiguracionFichajeService
{
    private const string ClaveCache = "config-fichaje";
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private readonly IConfiguracionFichajeRepositorio _repositorio;
    private readonly BitacoraService _bitacora;
    private readonly IMemoryCache _cache;
    private readonly FichajeOptions _opciones;
    private readonly IRolesRepositorio _roles;

    public ConfiguracionFichajeService(IConfiguracionFichajeRepositorio repositorio, BitacoraService bitacora,
        IMemoryCache cache, IOptions<FichajeOptions> opciones, IRolesRepositorio roles)
    {
        _roles = roles;
        _repositorio = repositorio;
        _bitacora = bitacora;
        _cache = cache;
        _opciones = opciones.Value;
    }

    public async Task<ConfiguracionFichaje> ObtenerAsync() =>
        (await _cache.GetOrCreateAsync(ClaveCache, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vida;
            var config = await _repositorio.ObtenerAsync() ?? new ConfiguracionFichaje
            {
                // Lo de appsettings (Fichaje:ModoUbicacion) sigue valiendo mientras nadie lo cambie desde la app
                Ubicacion = _opciones.ModoLibre ? ConfiguracionFichaje.UbicacionLibre : ConfiguracionFichaje.UbicacionSedeFija,
                Foto = ConfiguracionFichaje.FotoNo,
                PrecisionMaxMetros = 0 // sin elegir todavía: abajo se completa con Fichaje:PrecisionMaxMetros
            };
            config.RetencionFotosDias = _opciones.FotosRetencionDias;
            // Sin un valor elegido por el administrador rige appsettings (Fichaje:PrecisionMaxMetros)
            if (config.PrecisionMaxMetros <= 0) config.PrecisionMaxMetros = (int)Math.Round(_opciones.PrecisionMaxMetros);
            return config;
        }))!;

    public async Task GuardarAsync(string actor, GuardarConfiguracionFichajeRequest r)
    {
        var ubicacion = Elegir(r.Ubicacion, ConfiguracionFichaje.Ubicaciones, "ubicación");
        var foto = Elegir(r.Foto, ConfiguracionFichaje.Fotos, "foto");
        var modo = string.IsNullOrWhiteSpace(r.Modo) ? (await ObtenerAsync()).Modo : Elegir(r.Modo, ConfiguracionFichaje.Modos, "modo");

        var precision = r.PrecisionMaxMetros ?? (await ObtenerAsync()).PrecisionMaxMetros;
        if (precision is < ConfiguracionFichaje.PrecisionMinima or > ConfiguracionFichaje.PrecisionMaxima)
            throw new InvalidOperationException(
                $"La precisión del GPS tiene que estar entre {ConfiguracionFichaje.PrecisionMinima} y {ConfiguracionFichaje.PrecisionMaxima} metros.");

        await _repositorio.GuardarAsync(new ConfiguracionFichaje { Ubicacion = ubicacion, Foto = foto, Modo = modo, PrecisionMaxMetros = precision });
        _cache.Remove(ClaveCache);

        // El rol de las cuentas de sucursal no viene de fábrica: se crea la primera vez que se activa el kiosco
        if (modo == ConfiguracionFichaje.ModoKiosco && await RolKiosco.AsegurarAsync(_roles))
            await _bitacora.RegistrarAsync(actor, "ROL_KIOSCO_CREADO");

        await _bitacora.RegistrarAsync(actor, $"CONFIG_FICHAJE:{ubicacion}/{foto}/{modo}/{precision}m");
    }

    private static string Elegir(string? valor, string[] permitidos, string cual) =>
        permitidos.FirstOrDefault(p => p.Equals((valor ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"La opción de {cual} no es válida. Opciones: {string.Join(", ", permitidos)}.");
}
