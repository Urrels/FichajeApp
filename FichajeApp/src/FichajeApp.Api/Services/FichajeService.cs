using FichajeApp.Api.Models;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Storage;
using FichajeApp.Api.Validaciones;

namespace FichajeApp.Api.Services;

// Orquestador: arma el árbol de validaciones (Composite), delega la
// persistencia en la estrategia configurada (Strategy) y notifica a los
// observers suscriptos (Observer) una vez registrado el fichaje.
public class FichajeService
{
    private readonly IFichajeStorageStrategy _storage;
    private readonly List<IFichajeObserver> _observers;
    private readonly ValidacionCompuesta _validaciones;

    // "Libre": se registra dónde fichó (sitio conocido u "Otro lugar" con motivo).
    // Cualquier otro valor: modo anterior, sede fija validada con GeoValidator.
    public bool ModoLibre { get; }

    public FichajeService(IFichajeStorageStrategy storage, IEnumerable<IFichajeObserver> observers, IConfiguration config)
    {
        _storage = storage;
        _observers = observers.ToList();

        ModoLibre = string.Equals(config["Fichaje:ModoUbicacion"], "Libre", StringComparison.OrdinalIgnoreCase);
        var precisionMax = config.GetValue<double?>("Fichaje:PrecisionMaxMetros") ?? 50;

        // Composite: se arma una sola vez el árbol de reglas.
        _validaciones = new ValidacionCompuesta()
            .Agregar(new ValidacionEmpleadoActivo())
            .Agregar(new ValidacionUbicacionReal(precisionMax));

        if (ModoLibre)
            _validaciones.Agregar(new ValidacionMotivoSiLugarDesconocido());
        else
            _validaciones.Agregar(new ValidacionUbicacion());
    }

    public ResultadoValidacion Validar(FichajeContext contexto) => _validaciones.Validar(contexto);

    public async Task<FichajeDto> RegistrarAsync(EmpleadoDto empleado, FichajeContext ctx)
    {
        var tipoAnterior = await _storage.ObtenerUltimoTipoFichajeAsync(empleado.NombreCompleto);
        var tipo = tipoAnterior == "Entrada" ? "Salida" : "Entrada";

        var fichaje = new FichajeDto
        {
            NombreCompleto = empleado.NombreCompleto,
            Latitud = ctx.Latitud,
            Longitud = ctx.Longitud,
            FechaHora = DateTime.Now,
            TipoFichaje = tipo,
            Lugar = ctx.Sitio ?? (ModoLibre ? "Otro lugar" : "Sede"),
            Motivo = ctx.Motivo,
            Precision = ctx.Precision,
            Direccion = ctx.Direccion
        };

        await _storage.GuardarAsync(fichaje);

        foreach (var observer in _observers)
            observer.OnFichajeRegistrado(fichaje);

        return fichaje;
    }
}
