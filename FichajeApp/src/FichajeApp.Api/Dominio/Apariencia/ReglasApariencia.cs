using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FichajeApp.Api.Dominio.Apariencia;

// Reglas puras de la apariencia (colores y logo) que el administrador define desde la app.
public static partial class ReglasApariencia
{
    public const int LargoMaximoNombre = 40;
    public const int TamanioMaximoLogoBytes = 150 * 1024;
    public const double ContrasteMinimo = 3.0; // WCAG AA para texto grande/botones

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHex();

    // Devuelve el color normalizado ("#RRGGBB" en mayúsculas) o null si no es válido
    public static string? NormalizarColor(string? color)
    {
        var texto = (color ?? "").Trim();
        if (texto.Length == 6 && !texto.StartsWith('#')) texto = "#" + texto; // se acepta sin el #
        return ColorHex().IsMatch(texto) ? texto.ToUpperInvariant() : null;
    }

    // Luminancia relativa (WCAG 2.x) de un "#RRGGBB"
    public static double Luminancia(string color)
    {
        static double Canal(int valor)
        {
            var c = valor / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        int r = Convert.ToInt32(color.Substring(1, 2), 16);
        int g = Convert.ToInt32(color.Substring(3, 2), 16);
        int b = Convert.ToInt32(color.Substring(5, 2), 16);
        return 0.2126 * Canal(r) + 0.7152 * Canal(g) + 0.0722 * Canal(b);
    }

    // Relación de contraste entre dos colores, de 1 (iguales) a 21 (blanco sobre negro)
    public static double Contraste(string colorA, string colorB)
    {
        double la = Luminancia(colorA), lb = Luminancia(colorB);
        double claro = Math.Max(la, lb), oscuro = Math.Min(la, lb);
        return (claro + 0.05) / (oscuro + 0.05);
    }

    public enum FormatoLogo { Desconocido, Png, Jpeg }

    // Detecta el formato por los primeros bytes: no se confía en lo que diga el cliente
    public static FormatoLogo DetectarFormato(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return FormatoLogo.Png;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return FormatoLogo.Jpeg;
        return FormatoLogo.Desconocido;
    }

    public static string TipoMime(FormatoLogo formato) => formato == FormatoLogo.Png ? "image/png" : "image/jpeg";

    // Identifica la versión del logo: cambia cuando cambia la imagen, para que la app no use una copia vieja
    public static string VersionDe(byte[] logo) => Convert.ToHexString(SHA256.HashData(logo))[..12].ToLowerInvariant();
}
