using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Repositorios;

// Hoja Fichajes. Columnas A-J las escribe GoogleSheetsStorageStrategy al fichar:
//   Nombre | FechaHora | Lat | Lon | Tipo | Lugar | Motivo | Precisión | Dirección | Mapa
// La corrección agrega K Corregido | L MotivoCorreccion | M CorregidoPor | N FechaCorreccion.
// El identificador de un fichaje es su número de fila: no hay que borrar ni reordenar filas a mano.
public class FichajesSheetsRepositorio : IFichajesRepositorio
{
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly GoogleSheetsClient _cliente;
    private readonly HojaSheets _hoja;

    public FichajesSheetsRepositorio(GoogleSheetsClient cliente)
    {
        _cliente = cliente;
        _hoja = new HojaSheets(cliente, Planilla.Fichajes);
    }

    // No usa HojaSheets.LeerAsync porque la hoja puede no tener encabezado: la fila 1 también es un dato
    public async Task<List<FichajeRegistrado>> ListarAsync()
    {
        var pedido = _cliente.Service.Spreadsheets.Values.Get(_cliente.SpreadsheetId, $"{Planilla.Fichajes}!A:N");
        pedido.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var respuesta = await pedido.ExecuteAsync();

        var lista = new List<FichajeRegistrado>();
        if (respuesta.Values is null) return lista;

        for (int i = 0; i < respuesta.Values.Count; i++)
        {
            var fila = new FilaHoja(i + 1, respuesta.Values[i]);
            if (fila.Count < 5) continue;

            var fecha = fila.FechaONula(1);
            var tipo = fila.Texto(4);
            if (fecha is null || (tipo != "Entrada" && tipo != "Salida")) continue; // encabezado o fila rota

            lista.Add(new FichajeRegistrado
            {
                Id = fila.Numero,
                NombreCompleto = fila.Texto(0),
                FechaHora = fecha.Value,
                Tipo = tipo,
                Lugar = fila.Texto(5),
                Corregido = fila.Booleano(10),
                MotivoCorreccion = string.IsNullOrWhiteSpace(fila.Texto(11)) ? null : fila.Texto(11)
            });
        }
        return lista;
    }

    public async Task<FichajeRegistrado?> ObtenerAsync(int id) =>
        (await ListarAsync()).FirstOrDefault(f => f.Id == id);

    public async Task CorregirFechaHoraAsync(int id, DateTime nuevaFechaHora, string motivo, string corregidoPor, DateTime ahora)
    {
        await _escritura.WaitAsync();
        try
        {
            var actual = await ObtenerAsync(id) ?? throw new InvalidOperationException("El fichaje no existe.");

            // Se escribe la fecha como texto: queda igual que las que escribe la app al fichar
            await Actualizar($"B{id}", new List<object> { HojaSheets.FechaHora(nuevaFechaHora) });
            await Actualizar($"K{id}", new List<object>
            {
                true,
                Limpiar($"{motivo} [antes: {HojaSheets.FechaHora(actual.FechaHora)}]"),
                Limpiar(corregidoPor),
                HojaSheets.FechaHora(ahora)
            });
        }
        finally { _escritura.Release(); }
    }

    public async Task AgregarManualAsync(string empleado, string tipo, DateTime fechaHora, string motivo, string corregidoPor, DateTime ahora)
    {
        await _escritura.WaitAsync();
        try
        {
            var valores = new List<object>
            {
                Limpiar(empleado), HojaSheets.FechaHora(fechaHora), "", "", tipo, "Manual", Limpiar(motivo), "", "", "",
                true, Limpiar(motivo), Limpiar(corregidoPor), HojaSheets.FechaHora(ahora)
            };
            var pedido = _cliente.Service.Spreadsheets.Values.Append(new ValueRange { Values = new List<IList<object>> { valores } },
                _cliente.SpreadsheetId, $"{Planilla.Fichajes}!A:N");
            pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            pedido.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            await pedido.ExecuteAsync();
        }
        finally { _escritura.Release(); }
    }

    private async Task Actualizar(string celda, IList<object> valores)
    {
        var pedido = _cliente.Service.Spreadsheets.Values.Update(
            new ValueRange { Values = new List<IList<object>> { valores } },
            _cliente.SpreadsheetId, $"{Planilla.Fichajes}!{celda}");
        pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await pedido.ExecuteAsync();
    }

    private static string Limpiar(string texto) => texto.Trim();
}
