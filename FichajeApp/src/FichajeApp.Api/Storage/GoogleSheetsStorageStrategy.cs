using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Storage;

public class GoogleSheetsStorageStrategy : IFichajeStorageStrategy
{
    private readonly GoogleSheetsClient _sheets;
    private const string Range = "Fichajes!A:J"; // hoja "Fichajes"

    public GoogleSheetsStorageStrategy(GoogleSheetsClient sheets)
    {
        _sheets = sheets;
    }

    public async Task GuardarAsync(FichajeDto fichaje)
    {
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object>
                {
                    fichaje.NombreCompleto,
                    fichaje.FechaHora.ToString("yyyy-MM-dd HH:mm:ss"),
                    fichaje.Latitud,
                    fichaje.Longitud,
                    fichaje.TipoFichaje,
                    fichaje.Lugar,
                    fichaje.Motivo ?? "",
                    fichaje.Precision.HasValue ? Math.Round(fichaje.Precision.Value) : "",
                    fichaje.Direccion ?? "",
                    string.Create(CultureInfo.InvariantCulture,
                        $"https://www.google.com/maps?q={fichaje.Latitud},{fichaje.Longitud}")
                }
            }
        };

        var appendRequest = _sheets.Service.Spreadsheets.Values.Append(valueRange, _sheets.SpreadsheetId, Range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }

    public async Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto)
    {
        var request = _sheets.Service.Spreadsheets.Values.Get(_sheets.SpreadsheetId, Range);
        var response = await request.ExecuteAsync();

        if (response.Values is null || response.Values.Count == 0)
            return "Salida"; // no hay fichajes previos -> el próximo es "Entrada"

        var hoy = DateTime.Now.Date;

        var ultimoDeHoy = response.Values
            .Where(row => row.Count >= 5
                && row[0].ToString() == nombreCompleto
                && DateTime.TryParse(row[1].ToString(), out var fecha)
                && fecha.Date == hoy)
            .LastOrDefault();

        return ultimoDeHoy is null ? "Salida" : ultimoDeHoy[4].ToString()!;
    }
}
