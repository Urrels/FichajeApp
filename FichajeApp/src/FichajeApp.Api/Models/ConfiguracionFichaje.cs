namespace FichajeApp.Api.Models;

// Cómo ficha este cliente. Lo cambia el administrador desde la app (pestaña ConfigFichaje de la planilla);
// mientras no lo cambie se usa lo de appsettings (ubicación) y "sin foto".
public class ConfiguracionFichaje
{
    // Ninguna: no se pide ni se guarda la ubicación | Libre: se registra dónde fichó | SedeFija: solo dentro de la sede
    public const string UbicacionNinguna = "Ninguna";
    public const string UbicacionLibre = "Libre";
    public const string UbicacionSedeFija = "SedeFija";

    // No: sin foto | Opcional: se ofrece sacarla | Obligatoria: sin foto no se ficha
    public const string FotoNo = "No";
    public const string FotoOpcional = "Opcional";
    public const string FotoObligatoria = "Obligatoria";

    // Personal: cada persona ficha con su propio usuario en su teléfono (por defecto).
    // Kiosco: hay un teléfono por sucursal y cada empleado ficha en él con su nombre y su PIN.
    public const string ModoPersonal = "Personal";
    public const string ModoKiosco = "Kiosco";
    public static readonly string[] Modos = { ModoPersonal, ModoKiosco };

    public static readonly string[] Ubicaciones = { UbicacionNinguna, UbicacionLibre, UbicacionSedeFija };
    public static readonly string[] Fotos = { FotoNo, FotoOpcional, FotoObligatoria };

    public string Ubicacion { get; set; } = UbicacionLibre;
    public string Foto { get; set; } = FotoNo;
    public string Modo { get; set; } = ModoPersonal;

    // Cuánto tiempo se guardan las fotos (lo define el servidor, no el administrador); 0 = para siempre
    public int RetencionFotosDias { get; set; }
}

public class GuardarConfiguracionFichajeRequest
{
    public string? Ubicacion { get; set; }
    public string? Foto { get; set; }

    // Si no viene, se conserva el modo actual
    public string? Modo { get; set; }
}
