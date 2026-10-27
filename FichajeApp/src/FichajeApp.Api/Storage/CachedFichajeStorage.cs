using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Storage;

// Decorator sobre cualquier IFichajeStorageStrategy: evita releer toda la hoja
// "Fichajes" en cada fichaje. El último tipo del día se cachea por empleado y se
// actualiza al guardar. Si alguien edita la hoja a mano, el cambio se ve cuando
// vence la entrada (6 h) o se reinicia la API.
public class CachedFichajeStorage : IFichajeStorageStrategy
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
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
    }

    public async Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto)
    {
        return (await _cache.GetOrCreateAsync(Clave(nombreCompleto, DateTime.Now), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return _inner.ObtenerUltimoTipoFichajeAsync(nombreCompleto);
        }))!;
    }

    private static string Clave(string nombre, DateTime fecha) => $"ultimo:{nombre}:{fecha:yyyyMMdd}";
}
