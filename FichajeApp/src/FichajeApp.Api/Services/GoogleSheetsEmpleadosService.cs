using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Services;

// Lee el padrón de empleados autorizados desde la hoja "Empleados"
public class GoogleSheetsEmpleadosService
{
    private readonly SheetsService _sheetsService;
    private readonly string _spreadsheetId;
    private const string Range = "Empleados!A2:E"; // Id, NombreCompleto, Email, PinHash, Activo

    public GoogleSheetsEmpleadosService(IConfiguration config)
    {
        var credentialPath = config["GoogleSheets:CredentialsPath"];
        _spreadsheetId = config["GoogleSheets:SpreadsheetId"]!;

        var credential = GoogleCredential
            .FromFile(credentialPath)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FichajeApp"
        });
    }

    public async Task<List<EmpleadoDto>> ObtenerEmpleadosAsync()
    {
        var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, Range);
        var response = await request.ExecuteAsync();

        var empleados = new List<EmpleadoDto>();
        if (response.Values is null) return empleados;

        foreach (var row in response.Values)
        {
            if (row.Count < 5) continue;

            empleados.Add(new EmpleadoDto
            {
                Id = int.Parse(row[0].ToString()!),
                NombreCompleto = row[1].ToString()!,
                Email = row[2].ToString()!,
                PinHash = row[3].ToString()!,
                Activo = row[4].ToString()!.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            });
        }

        return empleados;
    }

    public async Task<EmpleadoDto?> BuscarPorEmailAsync(string email)
    {
        var empleados = await ObtenerEmpleadosAsync();
        return empleados.FirstOrDefault(e =>
            e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && e.Activo);
    }
}
