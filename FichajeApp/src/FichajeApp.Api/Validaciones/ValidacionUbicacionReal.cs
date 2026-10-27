using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Leaf del Composite: rechaza ubicaciones simuladas o con precisión insuficiente
public class ValidacionUbicacionReal : IReglaValidacion
{
    private readonly double _precisionMaxMetros;

    public ValidacionUbicacionReal(double precisionMaxMetros)
    {
        _precisionMaxMetros = precisionMaxMetros;
    }

    public ResultadoValidacion Validar(FichajeContext contexto)
    {
        if (contexto.EsUbicacionFalsa)
            return ResultadoValidacion.Error("Se detectó una ubicación simulada. Desactivá las apps de GPS falso.");

        if (contexto.Precision > _precisionMaxMetros)
            return ResultadoValidacion.Error(
                $"La ubicación no es lo bastante precisa (±{contexto.Precision:0} m, máximo {_precisionMaxMetros:0} m). " +
                "Salí a un lugar abierto y volvé a intentar.");

        return ResultadoValidacion.Ok();
    }
}
