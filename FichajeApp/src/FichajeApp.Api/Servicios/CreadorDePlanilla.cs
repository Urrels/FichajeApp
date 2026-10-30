using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Servicios;

// Crea la planilla del cliente desde cero (la crea la cuenta de servicio y se la comparte como Editor). Se reemplaza en los tests.
public interface ICreadorDePlanilla
{
    // Devuelve el ID de la planilla creada
    Task<string> CrearAsync(string credencialesJson, string titulo, string emailPropietario);

    // Para deshacer una planilla recién creada si la preparación falla
    Task EliminarAsync(string credencialesJson, string spreadsheetId);
}

public class CreadorDePlanillaGoogle : ICreadorDePlanilla
{
    private static (SheetsService Sheets, DriveService Drive) Servicios(string credencialesJson)
    {
        // drive.file: solo los archivos que crea esta misma aplicación (no ve nada más del Drive de la cuenta de servicio)
        var credencial = GoogleCredential.FromJson(credencialesJson).CreateScoped(SheetsService.Scope.Spreadsheets, DriveService.Scope.DriveFile);
        BaseClientService.Initializer Iniciar() => new()
        {
            HttpClientInitializer = new ReintentosPorCuota(credencial),
            ApplicationName = "FichajeApp"
        };
        return (new SheetsService(Iniciar()), new DriveService(Iniciar()));
    }

    public async Task<string> CrearAsync(string credencialesJson, string titulo, string emailPropietario)
    {
        var (sheets, drive) = Servicios(credencialesJson);
        using var _s = sheets;
        using var _d = drive;

        string id;
        try
        {
            var creada = await sheets.Spreadsheets.Create(new Spreadsheet
            {
                Properties = new SpreadsheetProperties { Title = titulo }
            }).ExecuteAsync();
            id = creada.SpreadsheetId;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new ConexionInvalidaException(EsApiSinActivar(ex)
                ? "La API de Google Sheets no está activada en el proyecto de Google Cloud de esta credencial. Activala en 'APIs y servicios' → 'Google Sheets API'."
                : "Google no dejó crear la planilla con esta cuenta de servicio. Creala vos a mano, compartila con el correo de la cuenta de servicio y pegá su link.");
        }

        try
        {
            var permiso = drive.Permissions.Create(new Permission { Type = "user", Role = "writer", EmailAddress = emailPropietario }, id);
            permiso.SendNotificationEmail = true;
            permiso.EmailMessage = "Esta es la planilla de tu sistema de fichaje. No la borres ni cambies el nombre de las pestañas.";
            await permiso.ExecuteAsync();
        }
        catch (Exception ex)
        {
            try { await drive.Files.Delete(id).ExecuteAsync(); } catch { /* la dejamos: no quedó compartida con nadie */ }

            throw new ConexionInvalidaException(ex is GoogleApiException g && EsApiSinActivar(g)
                ? "La API de Google Drive no está activada en el proyecto de Google Cloud de esta credencial. Activala en 'APIs y servicios' → 'Google Drive API' y volvé a intentar."
                : $"Se creó la planilla pero no se pudo compartir con {emailPropietario} (revisá que sea una cuenta de Google válida). No se guardó nada.");
        }
        return id;
    }

    public async Task EliminarAsync(string credencialesJson, string spreadsheetId)
    {
        var (sheets, drive) = Servicios(credencialesJson);
        using var _s = sheets;
        using var _d = drive;
        await drive.Files.Delete(spreadsheetId).ExecuteAsync();
    }

    private static bool EsApiSinActivar(GoogleApiException ex) =>
        ex.Message.Contains("has not been used", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("is disabled", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase);
}
