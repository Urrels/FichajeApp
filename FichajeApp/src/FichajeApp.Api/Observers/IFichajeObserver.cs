using FichajeApp.Api.Models;

namespace FichajeApp.Api.Observers;

// Patrón Observer: distintos módulos pueden "suscribirse" a que se registró
// un fichaje sin que FichajeService conozca sus detalles (logging, futuras
// notificaciones por email, auditoría, etc.). Se registran varias
// implementaciones vía DI (IEnumerable<IFichajeObserver>).
public interface IFichajeObserver
{
    void OnFichajeRegistrado(FichajeDto fichaje);
}
