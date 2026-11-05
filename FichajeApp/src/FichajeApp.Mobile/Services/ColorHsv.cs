namespace FichajeApp.Mobile.Services;

// Matemática de colores del selector de la pantalla de apariencia. Código puro (sin MAUI): lo compila también el proyecto de tests.
// H = tono (0 a 360), S = saturación (0 a 1), V = brillo (0 a 1). Los colores van como "#RRGGBB" en mayúsculas.
public static class ColorHsv
{
    public static (double H, double S, double V) Desde(string hex)
    {
        var (r, g, b) = Canales(hex);
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), delta = max - min;

        double h;
        if (delta == 0) h = 0;
        else if (max == rf) h = 60 * (((gf - bf) / delta) % 6);
        else if (max == gf) h = 60 * (((bf - rf) / delta) + 2);
        else h = 60 * (((rf - gf) / delta) + 4);
        if (h < 0) h += 360;

        return (h, max == 0 ? 0 : delta / max, max);
    }

    public static string AHex(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        static int Byte(double canal, double m) => (int)Math.Round((canal + m) * 255);
        return $"#{Byte(r, m):X2}{Byte(g, m):X2}{Byte(b, m):X2}";
    }

    // Luminancia relativa y contraste (WCAG): mismas cuentas que valida el servidor
    public static double Luminancia(string hex)
    {
        var (r, g, b) = Canales(hex);
        static double Canal(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Canal(r) + 0.7152 * Canal(g) + 0.0722 * Canal(b);
    }

    public static double Contraste(string a, string b)
    {
        double la = Luminancia(a), lb = Luminancia(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // Blanco o casi negro, el que mejor se lee sobre ese fondo
    public static string TextoLegible(string fondo) =>
        Contraste(fondo, "#FFFFFF") >= Contraste(fondo, "#212121") ? "#FFFFFF" : "#212121";

    private static (int R, int G, int B) Canales(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
