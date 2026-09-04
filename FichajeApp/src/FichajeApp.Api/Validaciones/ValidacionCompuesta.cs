using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Patrón Composite: agrupa varias reglas (hojas u otros compuestos) y las
// ejecuta a través de la misma interfaz IReglaValidacion. Agregar una regla
// nueva (ej. horario permitido, día laborable) es una línea más, sin tocar
// el código que las ejecuta ni el controller.
public class ValidacionCompuesta : IReglaValidacion
{
    private readonly List<IReglaValidacion> _reglas = new();

    public ValidacionCompuesta Agregar(IReglaValidacion regla)
    {
        _reglas.Add(regla);
        return this;
    }

    public ResultadoValidacion Validar(FichajeContext contexto)
    {
        foreach (var regla in _reglas)
        {
            var resultado = regla.Validar(contexto);
            if (!resultado.EsValido)
                return resultado; // corta en la primera regla que falla
        }

        return ResultadoValidacion.Ok();
    }
}
