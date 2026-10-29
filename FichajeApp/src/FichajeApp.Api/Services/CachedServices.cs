using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Services;

// Decorators (Open/Closed): agregan caché a un servicio existente sin modificarlo.

// Cachea la lista de empleados unos segundos: cada pedido autenticado y cada fichaje la consultan.
// Todas las lecturas salen de la misma lista y toda escritura la invalida, así el bloqueo por intentos
// fallidos y las bajas se ven enseguida. Devuelve copias: quien la usa modifica el objeto (por ejemplo el
// login suma un intento) y eso no debe tocar lo que quedó en la caché si la escritura falla.
// Vale para una sola instancia de la API; una edición manual de la planilla se ve al vencer la caché.
public class CachedEmpleadosRepositorio : IEmpleadosRepositorio
{
    private const string Clave = "empleados:lista";
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private readonly IEmpleadosRepositorio _inner;
    private readonly IMemoryCache _cache;

    public CachedEmpleadosRepositorio(IEmpleadosRepositorio inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<List<EmpleadoDto>> ListarAsync()
    {
        var lista = await _cache.GetOrCreateAsync(Clave, entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vida;
            return _inner.ListarAsync();
        });
        return lista!.Select(Copiar).ToList();
    }

    public async Task<EmpleadoDto?> ObtenerPorIdAsync(int id) =>
        (await ListarAsync()).FirstOrDefault(e => e.Id == id);

    public async Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false) =>
        (await ListarAsync()).FirstOrDefault(e =>
            e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && (incluirInactivos || e.Activo));

    public async Task<EmpleadoDto> CrearAsync(EmpleadoDto empleado)
    {
        try { return await _inner.CrearAsync(empleado); }
        finally { _cache.Remove(Clave); }
    }

    public async Task ActualizarAsync(EmpleadoDto empleado)
    {
        try { await _inner.ActualizarAsync(empleado); }
        finally { _cache.Remove(Clave); }
    }

    private static EmpleadoDto Copiar(EmpleadoDto e) => new()
    {
        Id = e.Id, NombreCompleto = e.NombreCompleto, Email = e.Email, PinHash = e.PinHash, Activo = e.Activo,
        Rol = e.Rol, IntentosFallidos = e.IntentosFallidos, Bloqueado = e.Bloqueado, Fila = e.Fila
    };
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
