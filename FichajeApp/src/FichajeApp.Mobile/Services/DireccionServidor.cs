namespace FichajeApp.Mobile.Services;

// Valida y deja en su forma final la dirección de la API que alguien escribe a mano en la app
// (ajuste oculto del login). Es código puro, sin nada de MAUI, así que también lo compila el proyecto de tests.
public static class DireccionServidor
{
    // Android bloquea http en el teléfono salvo hacia el emulador (ver network_security_config.xml)
    private static readonly string[] HostsParaHttp = { "10.0.2.2", "localhost", "127.0.0.1" };

    // Devuelve la dirección completa de la API (por ejemplo "https://algo.trycloudflare.com/api") o el motivo del rechazo
    public static (bool Valida, string Url, string Mensaje) Normalizar(string? texto)
    {
        var t = (texto ?? string.Empty).Trim();
        if (t.Length == 0)
            return (false, string.Empty, "Escribí la dirección del servidor.");

        // Si no trae https:// o http://, se asume https
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return (false, string.Empty, "Esa dirección no es válida. Ejemplo: https://mi-servidor.com");

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return (false, string.Empty, "La dirección tiene que empezar con https://");

        if (uri.Scheme == Uri.UriSchemeHttp && !HostsParaHttp.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return (false, string.Empty, "Android solo permite https:// (http:// funciona únicamente en el emulador).");

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return (false, string.Empty, "La dirección no puede llevar usuario, parámetros ni #.");

        var ruta = uri.AbsolutePath.TrimEnd('/');
        if (!ruta.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) ruta += "/api";

        return (true, uri.GetLeftPart(UriPartial.Authority) + ruta, string.Empty);
    }
}
