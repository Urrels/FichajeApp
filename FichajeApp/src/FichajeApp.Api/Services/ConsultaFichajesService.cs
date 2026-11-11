using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Services;

// Consultas de solo lectura sobre los fichajes registrados (pantalla de supervisor)
public class ConsultaFichajesService
{
    private readonly IFichajeStorageStrategy _storage;
    private readonly IFichajesRepositorio _repositorio;

    public ConsultaFichajesService(IFichajeStorageStrategy storage, IFichajesRepositorio repositorio)
    {
        _storage = storage;
        _repositorio = repositorio;
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

    // De quién es la foto de un fichaje (el nombre de la persona), o null si ningún fichaje la tiene
    public async Task<string?> ObtenerFotoDeAsync(string archivo)
    {
        if (string.IsNullOrWhiteSpace(archivo)) return null;
        var fichaje = (await _storage.ObtenerFichajesAsync()).FirstOrDefault(f => f.Foto == archivo);
        return fichaje?.NombreCompleto;
    }

    // Quién está "adentro": su último fichaje de hoy es una Entrada
    public async Task<List<FichajeDto>> ObtenerPresentesAsync()
    {
        var hoy = DateTime.Now.Date;
        var deHoy = await ObtenerAsync(hoy, hoy, null);

        // Salidas de hoy según el repositorio completo, que incluye las cargadas a mano por un administrador (la lista de
        // arriba no): si hay una posterior a la entrada, esa persona ya no está adentro. Sin esto, corregir un fichaje
        // abierto no lo cerraba en la pantalla.
        var manana = hoy.AddDays(1);
        var salidasManuales = (await _repositorio.ListarAsync())
            .Where(f => f.Tipo == "Salida" && f.FechaHora >= hoy && f.FechaHora < manana)
            .GroupBy(f => f.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(f => f.FechaHora), StringComparer.OrdinalIgnoreCase);

        return deHoy
            .GroupBy(f => f.NombreCompleto)
            .Select(g => g.Last())
            .Where(f => f.TipoFichaje == "Entrada")
            .Where(f => !salidasManuales.TryGetValue(f.NombreCompleto, out var salida) || salida <= f.FechaHora)
            .OrderBy(f => f.NombreCompleto)
            .ToList();
    }
}
