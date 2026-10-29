using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Storage;

// Decorator sobre cualquier IFichajeStorageStrategy: evita releer toda la hoja "Fichajes" en cada fichaje.
// El último tipo del día se cachea por empleado y se actualiza al guardar; las correcciones de un
// administrador lo invalidan (ver InvalidarUltimoTipo). Si alguien edita la hoja a mano, el cambio se ve
// cuando vence la entrada (6 h) o se reinicia la API. Vale para una sola instancia de la API.
public class CachedFichajeStorage : IFichajeStorageStrategy
{
    private const string ClaveTodos = "fichajes:todos";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private static readonly TimeSpan TtlConsulta = TimeSpan.FromSeconds(15);

    private readonly IFichajeStorageStrategy _inner;
    private readonly IMemoryCache _cache;

    public CachedFichajeStorage(IFichajeStorageStrategy inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task GuardarAsync(FichajeDto fichaje)
    {
        await _inner.GuardarAsync(fichaje);
        _cache.Set(Clave(fichaje.NombreCompleto, fichaje.FechaHora), fichaje.TipoFichaje, Ttl);
        _cache.Remove(ClaveTodos);
    }

    public async Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto)
    {
        return (await _cache.GetOrCreateAsync(Clave(nombreCompleto, DateTime.Now), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return _inner.ObtenerUltimoTipoFichajeAsync(nombreCompleto);
        }))!;
    }

    // Consulta del supervisor: la lista completa se cachea unos segundos para no leer la hoja en cada refresco
    public async Task<List<FichajeDto>> ObtenerFichajesAsync()
    {
        return (await _cache.GetOrCreateAsync(ClaveTodos, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TtlConsulta;
            return _inner.ObtenerFichajesAsync();
        }))!;
    }

    // Lo llaman las correcciones de fichajes: sin esto el próximo fichaje del empleado podría salir con el tipo equivocado
    public static void InvalidarUltimoTipo(IMemoryCache cache, string nombre, params DateTime[] fechas)
    {
        foreach (var fecha in fechas.Append(DateTime.Now).Select(f => f.Date).Distinct())
            cache.Remove(Clave(nombre, fecha));
        cache.Remove(ClaveTodos);
    }

    private static string Clave(string nombre, DateTime fecha) => $"ultimo:{nombre}:{fecha:yyyyMMdd}";
}
