using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace FichajeApp.Api.Infrastructure;

// Una sola conexión a Google Sheets compartida por todos los servicios de datos
// (antes cada servicio cargaba el JSON de credenciales por su cuenta).
// Es el punto de extensión para multi-cliente: hoy expone una planilla fija;
// cuando haya una planilla por cliente, esta es la pieza que pasa a resolverse por cliente.
public class GoogleSheetsClient
{
    public SheetsService Service { get; }
    public string SpreadsheetId { get; }

    public GoogleSheetsClient(IConfiguration config)
    {
        var credentialPath = config["GoogleSheets:CredentialsPath"];
        SpreadsheetId = config["GoogleSheets:SpreadsheetId"]!;

        var credential = GoogleCredential
            .FromFile(credentialPath)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        Service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FichajeApp"
        });
    }
}
