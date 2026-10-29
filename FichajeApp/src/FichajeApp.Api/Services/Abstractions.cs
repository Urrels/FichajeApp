using FichajeApp.Api.Models;

namespace FichajeApp.Api.Services;

// Abstracciones de los servicios de datos: los controllers y el dominio dependen
// de estas interfaces, no de Google Sheets (Dependency Inversion).

// Acceso a datos de los sitios (solo lee)
public interface ISitiosRepository
{
    Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync();
}

// Lógica de negocio sobre los sitios (qué sitio contiene una coordenada)
public interface ISitiosService
{
    Task<string?> BuscarSitioAsync(double lat, double lon);
}

public interface IFichajeService
{
    bool ModoLibre { get; }
    ResultadoValidacion Validar(FichajeContext contexto);
    Task<FichajeDto> RegistrarAsync(EmpleadoDto empleado, FichajeContext ctx);
}
