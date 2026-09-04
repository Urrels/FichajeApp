using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Component del patrón Composite: toda regla (simple o compuesta) se valida igual.
public interface IReglaValidacion
{
    ResultadoValidacion Validar(FichajeContext contexto);
}
