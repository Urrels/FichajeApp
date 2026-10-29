using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace FichajeApp.Api.Infrastructure;

public class ConexionNoConfiguradaException : InvalidOperationException
{
    public ConexionNoConfiguradaException()
        : base("La conexión con Google Sheets todavía no está configurada.") { }
}

// Una sola conexión a Google Sheets compartida por todos los servicios de datos.
// La credencial (cuenta de servicio) la pone quien instala el servidor, en GoogleSheets:CredentialsPath; el cliente
// solo comparte su planilla con esa cuenta y carga el link desde la app. Si no hay credencial en el servidor,
// la app también puede cargarla (AlmacenConexion). Se puede cambiar en caliente con Conectar: los servicios
// leen Service y SpreadsheetId en cada uso.
public class GoogleSheetsClient
{
    private const string IdDeEjemplo = "TU_SPREADSHEET_ID";

    private sealed record Conexion(SheetsService Service, string SpreadsheetId, string Correo);

    private volatile Conexion? _actual;
    private string? _credenciales;

    public GoogleSheetsClient(IConfiguration config, AlmacenConexion almacen, ILogger<GoogleSheetsClient> log)
    {
        try
        {
            var ruta = config["GoogleSheets:CredentialsPath"];
            if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
                _credenciales = File.ReadAllText(ruta);

            var guardada = almacen.Leer();
            if (guardada is not null)
            {
                Conectar(guardada.Value.SpreadsheetId, guardada.Value.CredencialesJson);
                return;
            }

            var id = config["GoogleSheets:SpreadsheetId"];
            if (_credenciales is not null && !string.IsNullOrWhiteSpace(id) && id != IdDeEjemplo)
                Conectar(id);
        }
        catch (Exception ex)
        {
            // La API arranca igual para poder reconfigurar la conexión desde la app
            log.LogError(ex, "No se pudo abrir la conexión con Google Sheets");
        }
    }

    public bool Configurado => _actual is not null;

    // La credencial que ya tiene el servidor (null si hay que cargarla desde la app)
    public bool TieneCredenciales => _credenciales is not null;
    public string? Credenciales => _credenciales;

    public SheetsService Service => (_actual ?? throw new ConexionNoConfiguradaException()).Service;
    public string SpreadsheetId => (_actual ?? throw new ConexionNoConfiguradaException()).SpreadsheetId;

    // Correo con el que el cliente tiene que compartir su planilla (no es un secreto)
    public string? CorreoCuentaServicio => _actual?.Correo ?? (_credenciales is null ? null : CorreoDe(_credenciales));

    public void Conectar(string spreadsheetId, string? credencialesJson = null)
    {
        var json = credencialesJson ?? _credenciales ?? throw new ConexionNoConfiguradaException();
        var (servicio, correo) = CrearServicio(json);
        _actual = new Conexion(servicio, spreadsheetId, correo);
        _credenciales = json;
    }

    private static string? CorreoDe(string json)
    {
        try { return System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("client_email").GetString(); }
        catch { return null; }
    }

    public static (SheetsService Servicio, string Correo) CrearServicio(string credencialesJson)
    {
        var correo = System.Text.Json.JsonDocument.Parse(credencialesJson).RootElement.GetProperty("client_email").GetString() ?? "";
        var credencial = GoogleCredential.FromJson(credencialesJson).CreateScoped(SheetsService.Scope.Spreadsheets);
        var servicio = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credencial,
            ApplicationName = "FichajeApp"
        });
        return (servicio, correo);
    }
}
