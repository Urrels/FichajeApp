using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace FichajeApp.Api.Infrastructure;

// Una fila leída de una pestaña. Numero es el número de fila real de la hoja (la 1 es el encabezado).
// Los valores se leen sin formato: los números y las fechas no dependen del idioma de la planilla.
public class FilaHoja
{
    public int Numero { get; }
    private readonly IList<object> _valores;

    public FilaHoja(int numero, IList<object> valores)
    {
        Numero = numero;
        _valores = valores;
    }

    public int Count => _valores.Count;

    public string Texto(int i) => i < _valores.Count ? _valores[i]?.ToString()?.Trim() ?? "" : "";

    public bool EstaVacia => _valores.All(v => string.IsNullOrWhiteSpace(v?.ToString()));

    public int Entero(int i) => (int)Math.Round(Decimal(i));

    public int? EnteroONulo(int i) => string.IsNullOrWhiteSpace(Texto(i)) ? null : Entero(i);

    public decimal Decimal(int i)
    {
        if (i >= _valores.Count) return 0m;
        return _valores[i] switch
        {
            double d => (decimal)d,
            string s when decimal.TryParse(s.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) => r,
            bool b => b ? 1m : 0m,
            _ => 0m
        };
    }

    public bool Booleano(int i) => i < _valores.Count && _valores[i] switch
    {
        bool b => b,
        string s => s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1",
        double d => d != 0,
        _ => false
    };

    // Acepta texto ISO ("2026-10-07", "2026-10-07 08:00:00") o el número de serie de Sheets
    public DateTime? FechaONula(int i)
    {
        if (i >= _valores.Count) return null;
        switch (_valores[i])
        {
            case double serie:
                var bruta = new DateTime(1899, 12, 30).AddDays(serie);
                return new DateTime((bruta.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f):
                return f;
            default:
                return null;
        }
    }

    public DateTime Fecha(int i) => FechaONula(i) ?? default;
}

// Acceso a una pestaña de la planilla. Todo lo nuevo se escribe en modo RAW: el texto del usuario
// (motivos, nombres) nunca se interpreta como fórmula de la planilla.
public class HojaSheets
{
    private readonly GoogleSheetsClient _cliente;
    public string Nombre { get; }

    public HojaSheets(GoogleSheetsClient cliente, string nombre)
    {
        _cliente = cliente;
        Nombre = nombre;
    }

    private SpreadsheetsResource.ValuesResource Valores => _cliente.Service.Spreadsheets.Values;

    // Lee todas las filas de datos (desde la 2). Las filas totalmente vacías se omiten.
    public async Task<List<FilaHoja>> LeerAsync(string columnas = "A:Z")
    {
        var pedido = Valores.Get(_cliente.SpreadsheetId, $"{Nombre}!{columnas}");
        pedido.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var respuesta = await pedido.ExecuteAsync();

        var filas = new List<FilaHoja>();
        if (respuesta.Values is null) return filas;

        for (int i = 1; i < respuesta.Values.Count; i++) // la fila 0 del resultado es el encabezado
        {
            var fila = new FilaHoja(i + 1, respuesta.Values[i]);
            if (!fila.EstaVacia) filas.Add(fila);
        }
        return filas;
    }

    public async Task AgregarAsync(IList<object> valores)
    {
        var pedido = Valores.Append(new ValueRange { Values = new List<IList<object>> { valores } },
            _cliente.SpreadsheetId, $"{Nombre}!A:A");
        pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
        pedido.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await pedido.ExecuteAsync();
    }

    public async Task ActualizarAsync(int fila, IList<object> valores, string primeraColumna = "A")
    {
        var pedido = Valores.Update(new ValueRange { Values = new List<IList<object>> { valores } },
            _cliente.SpreadsheetId, $"{Nombre}!{primeraColumna}{fila}");
        pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await pedido.ExecuteAsync();
    }

    // Reemplaza todas las filas de datos (se usa para tablas de relación chicas, como RolPermiso).
    // No es atómico: los servicios que lo usan se serializan con un lock.
    public async Task ReemplazarTodoAsync(IEnumerable<IList<object>> filas)
    {
        await Valores.Clear(new ClearValuesRequest(), _cliente.SpreadsheetId, $"{Nombre}!A2:Z").ExecuteAsync();

        var lista = filas.ToList();
        if (lista.Count == 0) return;

        var pedido = Valores.Update(new ValueRange { Values = lista },
            _cliente.SpreadsheetId, $"{Nombre}!A2");
        pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await pedido.ExecuteAsync();
    }

    public static string Fecha(DateTime f) => f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string FechaHora(DateTime f) => f.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
