namespace FichajeApp.Api.Models;

// Lo que el administrador define desde la app. Null = "usar el valor del cliente" (el que viene en la app).
public class AparienciaGuardada
{
    public string? Nombre { get; set; }
    public string? ColorPrimario { get; set; }
    public string? ColorPrimarioTexto { get; set; }
    public string? ColorAcento { get; set; }
    public byte[]? Logo { get; set; }
}

// Lo que ve cualquiera que abra la app, incluso antes de iniciar sesión (solo marca, nada sensible)
public class AparienciaPublicaDto
{
    public string? Nombre { get; set; }
    public string? ColorPrimario { get; set; }
    public string? ColorPrimarioTexto { get; set; }
    public string? ColorAcento { get; set; }
    public bool TieneLogo { get; set; }
    public string? VersionLogo { get; set; }
}

public class GuardarAparienciaRequest
{
    public string? Nombre { get; set; }
    public string? ColorPrimario { get; set; }
    public string? ColorPrimarioTexto { get; set; }
    public string? ColorAcento { get; set; }

    // Imagen PNG o JPEG en base64 (con o sin el prefijo "data:image/...;base64,"). Null = no cambia el logo.
    public string? LogoBase64 { get; set; }
    public bool QuitarLogo { get; set; }
}
