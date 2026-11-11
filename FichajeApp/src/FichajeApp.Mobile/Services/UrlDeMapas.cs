using System.Globalization;

namespace FichajeApp.Mobile.Services;

// Código puro (sin MAUI) para poder probarlo: la dirección de Google Maps con las coordenadas exactas
public static class UrlDeMapas
{
    // Siempre con punto decimal, sin importar el idioma del teléfono (con coma, Google Maps no entiende las coordenadas)
    public static string Google(double latitud, double longitud) =>
        string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/search/?api=1&query={latitud:0.######},{longitud:0.######}");
}
