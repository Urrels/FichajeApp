using System.Net.Http.Json;

namespace FichajeApp.Mobile.Services;

public class LoginRequest
{
    public string Email { get; set; } = default!;
    public string Pin { get; set; } = default!;
}

public class LoginResponse
{
    public string Token { get; set; } = default!;
    public string NombreCompleto { get; set; } = default!;
    public int EmpleadoId { get; set; }
    public string Rol { get; set; } = "Empleado";
    public List<string> Roles { get; set; } = new();
    public List<string> Permisos { get; set; } = new();
}

// Se encarga del I/O (llamar al backend, persistir en SecureStorage) y
// delega el estado en memoria de la sesión al Singleton SessionManager.
public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly IClienteService _clientes;

    private const string TokenKey = "auth_token";
    private const string NombreKey = "auth_nombre";
    private const string IdKey = "auth_id";
    private const string RolKey = "auth_rol";
    private const string PermisosKey = "auth_permisos";

    public AuthService(IClienteService clientes)
    {
        _httpClient = new HttpClient();
        _clientes = clientes;
    }

    // true si el último intento falló por falta de conexión (y no porque el servidor rechazó los datos)
    public bool UltimoFalloFueDeConexion { get; private set; }

    public async Task<(bool exito, string mensaje)> LoginAsync(string email, string pin)
    {
        UltimoFalloFueDeConexion = false;
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_clientes.BaseUrl}/auth/login",
                new LoginRequest { Email = email, Pin = pin });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                return (false, error?.GetValueOrDefault("mensaje") ?? "Error al iniciar sesión");
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (data is null) return (false, "Respuesta inválida del servidor");

            await SecureStorage.Default.SetAsync(TokenKey, data.Token);
            await SecureStorage.Default.SetAsync(NombreKey, data.NombreCompleto);
            await SecureStorage.Default.SetAsync(IdKey, data.EmpleadoId.ToString());
            await SecureStorage.Default.SetAsync(RolKey, data.Rol);
            await SecureStorage.Default.SetAsync(PermisosKey, string.Join("|", data.Permisos));

            // Actualiza el Singleton en memoria y notifica a los observers suscriptos
            SessionManager.Instance.IniciarSesion(data.Token, data.NombreCompleto, data.EmpleadoId, data.Rol, data.Permisos);

            return (true, "Login exitoso");
        }
        catch (Exception ex)
        {
            UltimoFalloFueDeConexion = true;
            return (false, TextosDeError.DeExcepcion(ex));
        }
    }

    // "Olvide mi PIN": pide el codigo por mail y despues lo canjea por un PIN nuevo (no hace falta sesion)
    public Task<(bool exito, string mensaje)> SolicitarCodigoAsync(string email) =>
        PostSinSesionAsync("auth/olvide-pin", new { email });

    public Task<(bool exito, string mensaje)> RestablecerPinAsync(string email, string codigo, string pinNuevo) =>
        PostSinSesionAsync("auth/restablecer-pin", new { email, codigo, pinNuevo });

    private async Task<(bool exito, string mensaje)> PostSinSesionAsync(string ruta, object cuerpo)
    {
        try
        {
            using var respuesta = await _httpClient.PostAsJsonAsync($"{_clientes.BaseUrl}/{ruta}", cuerpo);
            Dictionary<string, string>? datos = null;
            try { datos = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>(); } catch { }
            var mensaje = datos?.GetValueOrDefault("mensaje");
            if (respuesta.IsSuccessStatusCode) return (true, mensaje ?? "Listo");
            if ((int)respuesta.StatusCode == 429) return (false, "Demasiados intentos. Esper" + (char)0xE1 + " un minuto y volv" + (char)0xE9 + " a probar.");
            return (false, mensaje ?? TextosDeError.TiempoAgotado);
        }
        catch (Exception ex) { return (false, TextosDeError.DeExcepcion(ex)); }
    }

    public async Task<bool> RestaurarSesionAsync()
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        var nombre = await SecureStorage.Default.GetAsync(NombreKey);
        var idStr = await SecureStorage.Default.GetAsync(IdKey);

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(nombre) || !int.TryParse(idStr, out var id))
            return false;

        // Una sesión guardada por una versión anterior no tiene permisos: hay que volver a iniciar sesión
        var permisos = await SecureStorage.Default.GetAsync(PermisosKey);
        if (string.IsNullOrEmpty(permisos))
            return false;

        var rol = await SecureStorage.Default.GetAsync(RolKey) ?? "Empleado";
        SessionManager.Instance.IniciarSesion(token, nombre, id, rol, permisos.Split('|', StringSplitOptions.RemoveEmptyEntries));
        return true;
    }

    public void Logout()
    {
        // No se usa RemoveAll: los datos recordados para entrar con huella tienen que sobrevivir al cierre de sesión
        foreach (var clave in new[] { TokenKey, NombreKey, IdKey, RolKey, PermisosKey }) SecureStorage.Default.Remove(clave);
        SessionManager.Instance.FinalizarSesion();
    }

    public void NotificarSesionExpirada() => Logout();
}
