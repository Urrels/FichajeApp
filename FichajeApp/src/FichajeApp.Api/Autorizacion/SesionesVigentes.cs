using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace FichajeApp.Api.Autorizacion;

// Cómo está hoy una persona que presenta un token: si puede entrar y con qué permisos. Los permisos viajan dentro del token
// (12 h, o 30 días el kiosco), así que en cada pedido se comparan con los de ahora: quien perdió un permiso pierde también
// el acceso a lo que ese permiso abría, sin esperar a que venza el token.
public record SesionVigente(bool PuedeEntrar, HashSet<string> Permisos)
{
    public static readonly SesionVigente Denegada = new(false, new HashSet<string>());
}

// La consulta se cachea hasta 60 s por persona. Los cambios que importan (roles, permisos de un rol, baja, bloqueo)
// la invalidan al instante, así que 60 s es solo el techo para lo que se edite por fuera de la API.
public class SesionesVigentes
{
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly object _candado = new();
    private CancellationTokenSource _todas = new();

    public SesionesVigentes(IMemoryCache cache) => _cache = cache;

    public async Task<SesionVigente> ObtenerAsync(int empleadoId, Func<Task<SesionVigente>> cargar)
    {
        var sesion = await _cache.GetOrCreateAsync(Clave(empleadoId), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vida;
            entrada.AddExpirationToken(new CancellationChangeToken(TokenDeTodas()));
            return await cargar();
        });
        return sesion ?? SesionVigente.Denegada;
    }

    // Una persona: cambió sus roles, se la dio de baja, se la bloqueó o desbloqueó
    public void Invalidar(int empleadoId) => _cache.Remove(Clave(empleadoId));

    // Todas: cambió lo que permite un rol (puede afectar a mucha gente)
    public void InvalidarTodas()
    {
        CancellationTokenSource anterior;
        lock (_candado)
        {
            anterior = _todas;
            _todas = new CancellationTokenSource();
        }
        anterior.Cancel();
    }

    private CancellationToken TokenDeTodas()
    {
        lock (_candado) return _todas.Token;
    }

    private static string Clave(int empleadoId) => $"sesion:{empleadoId}";
}
