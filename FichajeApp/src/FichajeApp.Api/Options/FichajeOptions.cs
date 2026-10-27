namespace FichajeApp.Api.Options;

// Options pattern: configuración tipada de la sección "Fichaje" de appsettings.json
public class FichajeOptions
{
    public const string Seccion = "Fichaje";

    // "Libre": registra dónde fichó. Otro valor: modo anterior (sede fija con GeoValidator).
    public string ModoUbicacion { get; set; } = "Libre";
    public double PrecisionMaxMetros { get; set; } = 50;

    // Tiempo de vida en caché del padrón de empleados y de la hoja Sitios
    public int CacheMinutos { get; set; } = 2;

    public bool ModoLibre => string.Equals(ModoUbicacion, "Libre", StringComparison.OrdinalIgnoreCase);
}
