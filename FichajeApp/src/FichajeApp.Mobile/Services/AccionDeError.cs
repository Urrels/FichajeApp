namespace FichajeApp.Mobile.Services;

// Qué puede hacer la persona ante un aviso de error, para ofrecerle el botón justo en el mismo cartel:
//   - Si el problema se arregla en los ajustes del teléfono (permiso de ubicación o de cámara, GPS apagado): "Abrir ajustes".
//   - Si fue la conexión o una demora del servidor: "Reintentar".
//   - Si no hay nada que hacer desde el cartel (PIN mal, falta un dato): ningún botón.
// Es código puro (sin MAUI): se prueba desde el proyecto de tests.
public enum AccionDeError { Ninguna, AbrirAjustes, Reintentar }

public static class AccionDeErrorExtensiones
{
    public static AccionDeError Para(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return AccionDeError.Ninguna;

        // Primero lo de ajustes: un mismo texto puede hablar de "internet" y de "permiso", y lo que se arregla acá es el permiso
        if (Contiene(texto, "ajustes") || Contiene(texto, "gps está apagado") || Contiene(texto, "activá el gps")
            || Contiene(texto, "permiso de ubicación") || Contiene(texto, "permiso de la cámara"))
            return AccionDeError.AbrirAjustes;

        if (texto == TextosDeError.SinConexion || texto == TextosDeError.TiempoAgotado || texto == TextosDeError.ErrorGenerico)
            return AccionDeError.Reintentar;

        return AccionDeError.Ninguna;
    }

    public static string? Etiqueta(this AccionDeError accion) => accion switch
    {
        AccionDeError.AbrirAjustes => "Abrir ajustes",
        AccionDeError.Reintentar => "Reintentar",
        _ => null
    };

    private static bool Contiene(string texto, string buscado) => texto.Contains(buscado, StringComparison.OrdinalIgnoreCase);
}
