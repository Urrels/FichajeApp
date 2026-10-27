using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using FichajeApp.Api.Helpers;

namespace FichajeApp.Api.Services;

// Lee los lugares conocidos desde la hoja "Sitios" (Nombre, Latitud, Longitud, RadioMetros)
public class GoogleSheetsSitiosService
{
    private readonly SheetsService _sheetsService;
    private readonly string _spreadsheetId;
    private const string Range = "Sitios!A2:D";

    public GoogleSheetsSitiosService(IConfiguration config)
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

    // Devuelve el nombre del sitio más cercano que contiene la coordenada, o null
    public async Task<string?> BuscarSitioAsync(double lat, double lon)
    {
        var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, Range);
        // Valores crudos: evita problemas con coma decimal en planillas en español
        request.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var response = await request.ExecuteAsync();

        if (response.Values is null) return null;

        string? mejor = null;
        var mejorDistancia = double.MaxValue;

        foreach (var row in response.Values)
        {
            if (row.Count < 4) continue;

            double sLat, sLon, radio;
            try
            {
                sLat = Convert.ToDouble(row[1], CultureInfo.InvariantCulture);
                sLon = Convert.ToDouble(row[2], CultureInfo.InvariantCulture);
                radio = Convert.ToDouble(row[3], CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                continue; // fila mal cargada: se ignora
            }

            var distancia = GeoValidator.DistanciaMetros(lat, lon, sLat, sLon);
            if (distancia <= radio && distancia < mejorDistancia)
            {
                mejorDistancia = distancia;
                mejor = row[0]?.ToString();
            }
        }

        return mejor;
    }
}
