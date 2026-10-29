using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FichajeApp.Api.Infrastructure;

// Guarda en el servidor la planilla y la credencial que cargó el cliente desde la app. La credencial queda
// cifrada con Data Protection (las llaves se guardan junto al archivo) y nunca se manda al teléfono.
// También guarda la clave de instalación que protege la pantalla de configuración.
public class AlmacenConexion
{
    // La credencial es null cuando el servidor ya trae la suya (GoogleSheets:CredentialsPath): solo se guarda la planilla
    private sealed record Guardado(string SpreadsheetId, string? CredencialesProtegidas);

    private readonly string _archivo;
    private readonly string _archivoClave;
    private readonly IDataProtector _protector;
    private readonly string? _claveConfigurada;

    public AlmacenConexion(IConfiguration config, IHostEnvironment entorno, IDataProtectionProvider proteccion)
        : this(config["Setup:Carpeta"] ?? Path.Combine(entorno.ContentRootPath, "datos"), proteccion, config["Setup:Clave"]) { }

    public AlmacenConexion(string carpeta, IDataProtectionProvider proteccion, string? claveConfigurada = null)
    {
        Directory.CreateDirectory(carpeta);
        _archivo = Path.Combine(carpeta, "conexion.json");
        _archivoClave = Path.Combine(carpeta, "clave-instalacion.txt");
        _protector = proteccion.CreateProtector("FichajeApp.Conexion");
        _claveConfigurada = claveConfigurada;
    }

    public (string SpreadsheetId, string? CredencialesJson)? Leer()
    {
        if (!File.Exists(_archivo)) return null;

        var g = JsonSerializer.Deserialize<Guardado>(File.ReadAllText(_archivo));
        if (g is null) return null;
        return (g.SpreadsheetId, g.CredencialesProtegidas is null ? null : _protector.Unprotect(g.CredencialesProtegidas));
    }

    public void Guardar(string spreadsheetId, string? credencialesJson) =>
        File.WriteAllText(_archivo, JsonSerializer.Serialize(new Guardado(spreadsheetId,
            credencialesJson is null ? null : _protector.Protect(credencialesJson))));

    // Clave que hay que escribir en la app para configurar la conexión. Si no se definió Setup:Clave,
    // se genera una al azar la primera vez y queda en datos/clave-instalacion.txt (y en el log de la API).
    public string ClaveDeInstalacion()
    {
        if (!string.IsNullOrWhiteSpace(_claveConfigurada)) return _claveConfigurada;
        if (File.Exists(_archivoClave)) return File.ReadAllText(_archivoClave).Trim();

        var clave = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
        File.WriteAllText(_archivoClave, clave);
        return clave;
    }
}
