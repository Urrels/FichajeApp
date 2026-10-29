using FichajeApp.Api.Models;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Services;

// Consultas de solo lectura sobre los fichajes registrados (pantalla de supervisor)
public class ConsultaFichajesService
{
    private readonly IFichajeStorageStrategy _storage;

    public ConsultaFichajesService(IFichajeStorageStrategy storage)
    {
        _storage = storage;
    }

    // Fichajes entre dos fechas (inclusive), opcionalmente de un solo empleado, en orden cronológico
    public async Task<List<FichajeDto>> ObtenerAsync(DateTime desde, DateTime hasta, string? empleado)
    {
        var inicio = desde.Date;
        var fin = hasta.Date.AddDays(1);

        var todos = await _storage.ObtenerFichajesAsync();
        return todos
            .Where(f => f.FechaHora >= inicio && f.FechaHora < fin)
            .Where(f => string.IsNullOrWhiteSpace(empleado)
                        || f.NombreCompleto.Equals(empleado, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FechaHora)
            .ToList();
    }

    // Quién está "adentro": su último fichaje de hoy es una Entrada
    public async Task<List<FichajeDto>> ObtenerPresentesAsync()
    {
        var hoy = DateTime.Now.Date;
        var deHoy = await ObtenerAsync(hoy, hoy, null);

        return deHoy
            .GroupBy(f => f.NombreCompleto)
            .Select(g => g.Last())
            .Where(f => f.TipoFichaje == "Entrada")
            .OrderBy(f => f.NombreCompleto)
            .ToList();
    }
}
