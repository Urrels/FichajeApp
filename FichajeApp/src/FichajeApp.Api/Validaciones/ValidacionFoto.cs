using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Leaf del Composite: con foto obligatoria, sin foto no se ficha
public class ValidacionFoto : IReglaValidacion
{
    public ResultadoValidacion Validar(FichajeContext contexto) =>
        contexto.Foto is { Length: > 0 }
            ? ResultadoValidacion.Ok()
            : ResultadoValidacion.Error("Este fichaje requiere una foto. Sacate una foto y volvé a intentar.", "FOTO_REQUERIDA");
}
