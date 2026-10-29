using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Storage;

public class GoogleSheetsStorageStrategy : IFichajeStorageStrategy
{
    private readonly SheetsService _sheetsService;
    private readonly string _spreadsheetId;
    private const string Range = "Fichajes!A:J"; // hoja "Fichajes"

    public GoogleSheetsStorageStrategy(IConfiguration config)
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

        var appendRequest = _sheetsService.Spreadsheets.Values.Append(valueRange, _spreadsheetId, Range);
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
        var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, Range);
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

    private static bool TryNumero(object? valor, out double numero)
    {
        switch (valor)
        {
            case double d: numero = d; return true;
            case string s:
                return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numero);
            default: numero = 0; return false;
        }
    }

    // Sheets devuelve la fecha como número de serie (días desde 1899-12-30) o como texto
    private static bool TryFecha(object? valor, out DateTime fecha)
    {
        switch (valor)
        {
            case double serie:
                var bruta = new DateTime(1899, 12, 30).AddDays(serie);
                // El número de serie es un double: se redondea al segundo más cercano
                fecha = new DateTime((bruta.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
                return true;
            case string s:
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
            default: fecha = default; return false;
        }
    }

    public async Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto)
    {
        var request = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, Range);
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
