using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Infrastructure;
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
                    SinFormula(fichaje.Motivo),
                    fichaje.Precision.HasValue ? Math.Round(fichaje.Precision.Value) : "",
                    SinFormula(fichaje.Direccion),
                    string.Create(CultureInfo.InvariantCulture,
                        $"https://www.google.com/maps?q={fichaje.Latitud},{fichaje.Longitud}")
                }
            }
        };

        var appendRequest = _sheets.Service.Spreadsheets.Values.Append(valueRange, _sheets.SpreadsheetId, Range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }

    // Se escribe con USERENTERED: un texto del usuario que empiece con = + - @ se ejecutaría como fórmula
    // de la planilla (por ejemplo =IMPORTXML). El apóstrofe inicial lo fuerza a quedar como texto.
    private static string SinFormula(string? texto)
    {
        texto = texto?.Trim() ?? "";
        return texto.Length > 0 && "=+-@".Contains(texto[0]) ? "'" + texto : texto;
    }

    public async Task<List<FichajeDto>> ObtenerFichajesAsync()
    {
        var request = _sheets.Service.Spreadsheets.Values.Get(_sheets.SpreadsheetId, Range);
        // Valores crudos: números y fechas no dependen del idioma de la planilla
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var response = await request.ExecuteAsync();

        var fichajes = new List<FichajeDto>();
        if (response.Values is null) return fichajes;

        foreach (var row in response.Values)
        {
            if (row.Count < 5) continue;
            if (!TryFecha(row[1], out var fecha) || !TryNumero(row[2], out var lat) || !TryNumero(row[3], out var lon))
                continue; // encabezado o fila mal cargada

            fichajes.Add(new FichajeDto
            {
                NombreCompleto = row[0]?.ToString() ?? "",
                FechaHora = fecha,
                Latitud = lat,
                Longitud = lon,
                TipoFichaje = row[4]?.ToString() ?? "",
                Lugar = row.Count > 5 ? row[5]?.ToString() ?? "" : "",
                Motivo = row.Count > 6 && !string.IsNullOrWhiteSpace(row[6]?.ToString()) ? row[6].ToString() : null,
                Precision = row.Count > 7 && TryNumero(row[7], out var prec) ? prec : null,
                Direccion = row.Count > 8 && !string.IsNullOrWhiteSpace(row[8]?.ToString()) ? row[8].ToString() : null
            });
        }

        return fichajes;
    }

    // Los enteros llegan como long y los decimales como double: la conversión compartida contempla ambos
    private static bool TryNumero(object? valor, out double numero)
    {
        var ok = ValoresCelda.TryNumero(valor, out var n);
        numero = (double)n;
        return ok;
    }

    private static bool TryFecha(object? valor, out DateTime fecha)
    {
        fecha = ValoresCelda.TryFecha(valor) ?? default;
        return fecha != default;
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
