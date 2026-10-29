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
// La conexión se resuelve en este orden: la que se guardó desde la pantalla de configuración de la app
// (AlmacenConexion) y, si no hay, la de appsettings (GoogleSheets:CredentialsPath / SpreadsheetId).
// Se puede cambiar en caliente con Conectar: los servicios leen Service y SpreadsheetId en cada uso.
public class GoogleSheetsClient
{
    private const string IdDeEjemplo = "TU_SPREADSHEET_ID";

    private sealed record Conexion(SheetsService Service, string SpreadsheetId, string Correo);

    private volatile Conexion? _actual;

    public GoogleSheetsClient(IConfiguration config, AlmacenConexion almacen, ILogger<GoogleSheetsClient> log)
    {
        try
        {
            var guardada = almacen.Leer();
            if (guardada is not null)
            {
                Conectar(guardada.Value.SpreadsheetId, guardada.Value.CredencialesJson);
                return;
            }

            var ruta = config["GoogleSheets:CredentialsPath"];
            var id = config["GoogleSheets:SpreadsheetId"];
            if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta) && !string.IsNullOrWhiteSpace(id) && id != IdDeEjemplo)
                Conectar(id, File.ReadAllText(ruta));
        }
        catch (Exception ex)
        {
            // La API arranca igual para poder reconfigurar la conexión desde la app
            log.LogError(ex, "No se pudo abrir la conexión con Google Sheets");
        }
    }

    public bool Configurado => _actual is not null;

    public SheetsService Service => (_actual ?? throw new ConexionNoConfiguradaException()).Service;
    public string SpreadsheetId => (_actual ?? throw new ConexionNoConfiguradaException()).SpreadsheetId;
    public string? CorreoCuentaServicio => _actual?.Correo;

    public void Conectar(string spreadsheetId, string credencialesJson)
    {
        var (servicio, correo) = CrearServicio(credencialesJson);
        _actual = new Conexion(servicio, spreadsheetId, correo);
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
