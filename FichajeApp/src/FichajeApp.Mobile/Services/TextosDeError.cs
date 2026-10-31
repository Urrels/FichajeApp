namespace FichajeApp.Mobile.Services;

public enum Severidad { Info, Exito, Aviso, Error }

// Todo lo que la persona lee cuando algo sale mal (o bien), en un solo lugar y en un solo tono.
//   - Qué pasó + qué puede hacer; sin culpar, sin códigos y sin jerga.
//   - Voseo, frases cortas (hasta dos líneas) y botones con verbo.
//   - NUNCA se muestra el texto técnico de una excepción ("Unable to resolve host…"): se traduce según su tipo.
// Es código puro (sin MAUI): se prueba desde el proyecto de tests.
public static class TextosDeError
{
    public const string SinConexion = "No pudimos conectarnos. Revisá tu internet y probá de nuevo.";
    public const string TiempoAgotado = "El servidor tardó demasiado en responder. Probá de nuevo en un momento.";
    public const string SesionVencida = "Por seguridad cerramos tu sesión. Entrá de nuevo para seguir.";
    public const string ErrorGenerico = "Algo salió mal de nuestro lado. Probá de nuevo en un momento.";
    public const string SinPermiso = "Tu usuario no tiene permiso para hacer esto.";
    public const string FichajeSinConfirmar =
        "El servidor tardó en responder. Antes de volver a fichar, revisá si tu fichaje quedó registrado.";
    public const string CamaraNoDisponible = "No pudimos usar la cámara. Revisá el permiso en Ajustes y probá de nuevo.";
    public const string UbicacionNoDisponible = "No pudimos obtener tu ubicación. Activá el GPS y probá de nuevo al aire libre.";
    public const string ImagenNoValida = "No pudimos usar esa imagen. Probá con otra (JPG o PNG).";
    public const string ArchivoNoValido = "No pudimos leer ese archivo. Elegí el .json de la cuenta de servicio.";

    // Traduce una excepción según su tipo; el detalle técnico queda para el log, no para la pantalla
    public static string DeExcepcion(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => TiempoAgotado,   // incluye TaskCanceledException
        HttpRequestException or System.Net.Sockets.SocketException or IOException => SinConexion,
        _ => ErrorGenerico
    };

    // Los códigos que manda el servidor se traducen a frases amables; si no hay código conocido se usa el mensaje del servidor
    // (que ya está escrito para personas) y, si tampoco hay, una frase genérica.
    public static string Para(string? codigo, string? delServidor) => codigo switch
    {
        "PIN_INCORRECTO"     => "Ese PIN no coincide. Probá de nuevo.",
        "BLOQUEADO"          => "Tu usuario quedó bloqueado por seguridad. Pedile a un administrador que lo desbloquee.",
        "FOTO_REQUERIDA"     => "Para fichar necesitamos una foto. Sacate una y listo.",
        "FOTO_INVALIDA"      => ImagenNoValida,
        "SIN_PERMISO"        => SinPermiso,
        "KIOSCO_DESACTIVADO" => "El modo kiosco no está activado. Un administrador puede prenderlo en «Cómo se ficha».",
        _ => string.IsNullOrWhiteSpace(delServidor) ? ErrorGenerico : delServidor
    };

    // Éxito del fichaje: es el momento clave de la app, merece una confirmación clara con la hora
    public static string FichajeRegistrado(string tipo, DateTime hora) => $"¡Listo! {tipo} registrada a las {hora:HH:mm}.";
}
