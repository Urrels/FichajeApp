using FichajeApp.Api.Models;

namespace FichajeApp.Api.Storage;

// Patrón Strategy: desacopla el "cómo" se persiste el fichaje del resto del
// sistema. Hoy hay una sola implementación (Google Sheets), pero el
// controller/servicio no depende de ella directamente, sino de esta interfaz.
// Mañana se puede sumar OneDriveStorageStrategy (Graph API) sin tocar nada más.
public interface IFichajeStorageStrategy
{
    Task GuardarAsync(FichajeDto fichaje);
    Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto);

    // Todos los fichajes registrados (para las consultas del supervisor)
    Task<List<FichajeDto>> ObtenerFichajesAsync();
}
