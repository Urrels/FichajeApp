using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;

namespace FichajeApp.Api.Services;

// Decorators (Open/Closed): agregan caché a un servicio existente sin modificarlo.

public class CachedEmpleadosService : IEmpleadosService
{
    private readonly IEmpleadosService _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedEmpleadosService(IEmpleadosService inner, IMemoryCache cache, IOptions<FichajeOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromMinutes(options.Value.CacheMinutos);
    }

    public async Task<EmpleadoDto?> BuscarPorEmailAsync(string email)
    {
        return await _cache.GetOrCreateAsync($"empleado:{email.ToLowerInvariant()}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.BuscarPorEmailAsync(email);
        });
    }
}

public class CachedSitiosRepository : ISitiosRepository
{
    private const string Clave = "sitios";
    private readonly ISitiosRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedSitiosRepository(ISitiosRepository inner, IMemoryCache cache, IOptions<FichajeOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromMinutes(options.Value.CacheMinutos);
    }

    public async Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync()
    {
        return (await _cache.GetOrCreateAsync(Clave, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.ObtenerSitiosAsync();
        }))!;
    }
}
