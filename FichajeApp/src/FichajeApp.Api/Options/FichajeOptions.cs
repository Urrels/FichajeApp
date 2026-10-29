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

    // Días que se conservan las fotos de los fichajes; pasado ese tiempo se borran solas (0 = no borrar nunca)
    public int FotosRetencionDias { get; set; } = 90;

    public bool ModoLibre => string.Equals(ModoUbicacion, "Libre", StringComparison.OrdinalIgnoreCase);
}
