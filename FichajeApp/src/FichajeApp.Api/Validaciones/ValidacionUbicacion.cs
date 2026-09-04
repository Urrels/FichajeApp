using FichajeApp.Api.Helpers;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Validaciones;

// Leaf del Composite
public class ValidacionUbicacion : IReglaValidacion
{
    public ResultadoValidacion Validar(FichajeContext contexto)
    {
        return GeoValidator.EstaEnRango(contexto.Latitud, contexto.Longitud)
            ? ResultadoValidacion.Ok()
            : ResultadoValidacion.Error("Fuera del rango permitido para fichar");
    }
}
