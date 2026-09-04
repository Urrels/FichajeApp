using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Leaf del Composite
public class ValidacionEmpleadoActivo : IReglaValidacion
{
    public ResultadoValidacion Validar(FichajeContext contexto)
    {
        if (contexto.Empleado is null || !contexto.Empleado.Activo)
            return ResultadoValidacion.Error("Empleado no autorizado o inactivo");

        return ResultadoValidacion.Ok();
    }
}
