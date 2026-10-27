using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Services;

// Lee el padrón de empleados autorizados desde la hoja "Empleados"
public class GoogleSheetsEmpleadosService : IEmpleadosService
{
    private readonly GoogleSheetsClient _sheets;
    private const string Range = "Empleados!A2:E"; // Id, NombreCompleto, Email, PinHash, Activo

    public GoogleSheetsEmpleadosService(GoogleSheetsClient sheets)
    {
        _sheets = sheets;
    }

    public async Task<List<EmpleadoDto>> ObtenerEmpleadosAsync()
    {
        var request = _sheets.Service.Spreadsheets.Values.Get(_sheets.SpreadsheetId, Range);
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
