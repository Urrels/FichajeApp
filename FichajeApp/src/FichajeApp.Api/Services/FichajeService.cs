using Microsoft.Extensions.Options;
using FichajeApp.Api.Concurrency;
using FichajeApp.Api.Models;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Options;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Storage;
using FichajeApp.Api.Validaciones;

namespace FichajeApp.Api.Services;

// Orquestador: arma el árbol de validaciones (Composite) según la configuración del cliente, delega la
// persistencia en la estrategia configurada (Strategy) y notifica a los observers suscriptos (Observer)
// una vez registrado el fichaje.
public class FichajeService : IFichajeService
{
    private readonly IFichajeStorageStrategy _storage;
    private readonly List<IFichajeObserver> _observers;
    private readonly BloqueoPorClave _bloqueo;
    private readonly ConfiguracionFichajeService _configuracion;
    private readonly IAlmacenFotos _fotos;
    private readonly double _precisionMaxMetros;

    public FichajeService(IFichajeStorageStrategy storage, IEnumerable<IFichajeObserver> observers,
        BloqueoPorClave bloqueo, ConfiguracionFichajeService configuracion, IAlmacenFotos fotos,
        IOptions<FichajeOptions> options)
    {
        _storage = storage;
        _observers = observers.ToList();
        _bloqueo = bloqueo;
        _configuracion = configuracion;
        _fotos = fotos;
        _precisionMaxMetros = options.Value.PrecisionMaxMetros;
    }

    public Task<ConfiguracionFichaje> ConfiguracionAsync() => _configuracion.ObtenerAsync();

    // Las reglas dependen de lo que el administrador configuró (ubicación y foto)
    public async Task<ResultadoValidacion> ValidarAsync(FichajeContext contexto)
    {
        var config = await _configuracion.ObtenerAsync();

        var reglas = new ValidacionCompuesta().Agregar(new ValidacionEmpleadoActivo());

        if (config.Ubicacion != ConfiguracionFichaje.UbicacionNinguna)
            reglas.Agregar(new ValidacionUbicacionReal(_precisionMaxMetros));

        if (config.Ubicacion == ConfiguracionFichaje.UbicacionLibre)
            reglas.Agregar(new ValidacionMotivoSiLugarDesconocido());
        else if (config.Ubicacion == ConfiguracionFichaje.UbicacionSedeFija)
            reglas.Agregar(new ValidacionUbicacion());

        if (config.Foto == ConfiguracionFichaje.FotoObligatoria)
            reglas.Agregar(new ValidacionFoto());

        return reglas.Validar(contexto);
    }

    public async Task<string> ProximoTipoAsync(string nombreCompleto) =>
        await _storage.ObtenerUltimoTipoFichajeAsync(nombreCompleto) == "Entrada" ? "Salida" : "Entrada";

    public async Task<FichajeDto> RegistrarAsync(EmpleadoDto empleado, FichajeContext ctx)
    {
        var config = await _configuracion.ObtenerAsync();

        // Un fichaje por empleado a la vez: leer el último tipo y guardar es una sola sección crítica
        using var _ = await _bloqueo.AdquirirAsync(empleado.NombreCompleto);

        var tipo = await ProximoTipoAsync(empleado.NombreCompleto);
        var ahora = DateTime.Now;
        var sinUbicacion = config.Ubicacion == ConfiguracionFichaje.UbicacionNinguna;

        var fichaje = new FichajeDto
        {
            NombreCompleto = empleado.NombreCompleto,
            Latitud = sinUbicacion ? 0 : ctx.Latitud,
            Longitud = sinUbicacion ? 0 : ctx.Longitud,
            FechaHora = ahora,
            TipoFichaje = tipo,
            Lugar = sinUbicacion ? "Sin ubicación"
                : ctx.Sitio ?? (config.Ubicacion == ConfiguracionFichaje.UbicacionLibre ? "Otro lugar" : "Sede"),
            Motivo = ctx.Motivo,
            Precision = sinUbicacion ? null : ctx.Precision,
            Direccion = sinUbicacion ? null : ctx.Direccion
        };

        // Si la foto no se puede guardar, el fichaje no se registra: no queda un fichaje "con foto" sin foto
        if (config.Foto != ConfiguracionFichaje.FotoNo && ctx.Foto is { Length: > 0 })
            fichaje.Foto = await _fotos.GuardarAsync(ctx.Foto, ahora);

        await _storage.GuardarAsync(fichaje);

        foreach (var observer in _observers)
            observer.OnFichajeRegistrado(fichaje);

        return fichaje;
    }
}
