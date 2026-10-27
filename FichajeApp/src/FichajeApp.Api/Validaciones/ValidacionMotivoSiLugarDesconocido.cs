using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Leaf del Composite: fuera de un sitio conocido se exige un motivo
public class ValidacionMotivoSiLugarDesconocido : IReglaValidacion
{
    public ResultadoValidacion Validar(FichajeContext contexto)
    {
        if (contexto.Sitio is null && string.IsNullOrWhiteSpace(contexto.Motivo))
            return ResultadoValidacion.Error(
                "Estás fuera de un lugar conocido. Indicá el motivo del fichaje.", "MOTIVO_REQUERIDO");

        return ResultadoValidacion.Ok();
    }
}
