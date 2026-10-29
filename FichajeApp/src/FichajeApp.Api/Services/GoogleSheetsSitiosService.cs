using System.Globalization;
using Google.Apis.Sheets.v4;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Services;

// Lee los lugares conocidos desde la hoja "Sitios" (Nombre, Latitud, Longitud, RadioMetros)
public class GoogleSheetsSitiosService : ISitiosRepository
{
    private readonly GoogleSheetsClient _sheets;
    private const string Range = "Sitios!A2:D";

    public GoogleSheetsSitiosService(GoogleSheetsClient sheets)
    {
        _sheets = sheets;
    }

    public async Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync()
    {
        var request = _sheets.Service.Spreadsheets.Values.Get(_sheets.SpreadsheetId, Range);
        // Valores crudos: evita problemas con coma decimal en planillas en español
        request.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var response = await request.ExecuteAsync();

        var sitios = new List<Sitio>();
        if (response.Values is null) return sitios;

        foreach (var row in response.Values)
        {
            if (row.Count < 4) continue;

            try
            {
                sitios.Add(new Sitio(
                    row[0]?.ToString() ?? "",
                    Convert.ToDouble(row[1], CultureInfo.InvariantCulture),
                    Convert.ToDouble(row[2], CultureInfo.InvariantCulture),
                    Convert.ToDouble(row[3], CultureInfo.InvariantCulture)));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // fila mal cargada: se ignora
            }
        }

        return sitios;
    }
}
