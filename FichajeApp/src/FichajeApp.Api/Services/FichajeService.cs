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

    public FichajeService(IFichajeStorageStrategy storage, IEnumerable<IFichajeObserver> observers)
    {
        _storage = storage;
        _observers = observers.ToList();

        // Composite: se arma una sola vez el árbol de reglas.
        _validaciones = new ValidacionCompuesta()
            .Agregar(new ValidacionEmpleadoActivo())
            .Agregar(new ValidacionUbicacion());
    }

    public ResultadoValidacion Validar(FichajeContext contexto) => _validaciones.Validar(contexto);

    public async Task<FichajeDto> RegistrarAsync(EmpleadoDto empleado, double lat, double lon)
    {
        var tipoAnterior = await _storage.ObtenerUltimoTipoFichajeAsync(empleado.NombreCompleto);
        var tipo = tipoAnterior == "Entrada" ? "Salida" : "Entrada";

        var fichaje = new FichajeDto
        {
            NombreCompleto = empleado.NombreCompleto,
            Latitud = lat,
            Longitud = lon,
            FechaHora = DateTime.Now,
            TipoFichaje = tipo
        };

        await _storage.GuardarAsync(fichaje);

        foreach (var observer in _observers)
            observer.OnFichajeRegistrado(fichaje);

        return fichaje;
    }
}
