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

    public async Task<(bool exito, string mensaje)> LoginAsync(string email, string pin)
    {
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
            return (false, $"Error de conexión: {ex.Message}");
        }
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
        SecureStorage.Default.RemoveAll();
        SessionManager.Instance.FinalizarSesion();
    }

    public void NotificarSesionExpirada() => Logout();
}
