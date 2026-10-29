using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FichajeApp.Mobile.Services;

public class FichajeRequest
{
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
    public bool EsUbicacionFalsa { get; set; }
    public string? Direccion { get; set; }
    public string? Motivo { get; set; }
}

public record ResultadoFichaje(bool Exito, string Mensaje, bool RequiereMotivo = false);

public record ResultadoConsulta<T>(bool Exito, string Mensaje, T? Datos);

// Un fichaje tal como lo devuelve la API de supervisor
public class FichajeItem
{
    public string NombreCompleto { get; set; } = string.Empty;
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public DateTime FechaHora { get; set; }
    public string TipoFichaje { get; set; } = string.Empty;
    public string Lugar { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public double? Precision { get; set; }
    public string? Direccion { get; set; }

    public string Resumen =>
        $"{FechaHora:dd/MM HH:mm} · {TipoFichaje} · {Lugar}" + (string.IsNullOrWhiteSpace(Motivo) ? "" : $" · {Motivo}");
}

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly IClienteService _clientes;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public ApiService(AuthService authService, IClienteService clientes)
    {
        _httpClient = new HttpClient();
        _authService = authService;
        _clientes = clientes;
    }

    // --- Consultas de supervisor ---

    public Task<ResultadoConsulta<List<FichajeItem>>> ObtenerFichajesAsync(DateTime desde, DateTime hasta, string? empleado)
    {
        var ruta = $"supervisor/fichajes?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(empleado))
            ruta += $"&empleado={Uri.EscapeDataString(empleado)}";
        return GetAsync<List<FichajeItem>>(ruta);
    }

    public Task<ResultadoConsulta<List<FichajeItem>>> ObtenerPresentesAsync() =>
        GetAsync<List<FichajeItem>>("supervisor/presentes");

    public Task<ResultadoConsulta<List<string>>> ObtenerEmpleadosAsync() =>
        GetAsync<List<string>>("supervisor/empleados");

    private async Task<ResultadoConsulta<T>> GetAsync<T>(string ruta)
    {
        try
        {
            var token = SessionManager.Instance.Token;
            if (string.IsNullOrEmpty(token))
            {
                _authService.NotificarSesionExpirada();
                return new ResultadoConsulta<T>(false, "Sesión expirada, iniciá sesión de nuevo", default);
            }

            // Header por request: el HttpClient es compartido y no se muta
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_clientes.BaseUrl}/{ruta}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _authService.NotificarSesionExpirada();
                return new ResultadoConsulta<T>(false, "Sesión expirada, iniciá sesión de nuevo", default);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return new ResultadoConsulta<T>(false, "No tenés permiso de supervisor", default);

            if (!response.IsSuccessStatusCode)
            {
                // El error puede venir como JSON {mensaje} o como texto: no asumir el formato
                var cuerpo = await response.Content.ReadAsStringAsync();
                string? mensaje = null;
                try { mensaje = System.Text.Json.JsonDocument.Parse(cuerpo).RootElement.GetProperty("mensaje").GetString(); }
                catch { /* no era JSON con "mensaje" */ }
                return new ResultadoConsulta<T>(false, mensaje ?? $"El servidor respondió con error ({(int)response.StatusCode})", default);
            }

            var datos = await response.Content.ReadFromJsonAsync<T>();
            return new ResultadoConsulta<T>(true, "OK", datos);
        }
        catch (OperationCanceledException)
        {
            return new ResultadoConsulta<T>(false, "El servidor no respondió a tiempo", default);
        }
        catch (Exception ex)
        {
            return new ResultadoConsulta<T>(false, $"Error de conexión: {ex.Message}", default);
        }
    }

    public async Task<ResultadoFichaje> RegistrarFichajeAsync(FichajeRequest fichaje)
    {
        try
        {
            var token = SessionManager.Instance.Token;
            if (string.IsNullOrEmpty(token))
            {
                _authService.NotificarSesionExpirada();
                return new ResultadoFichaje(false, "Sesión expirada, iniciá sesión de nuevo");
            }

            // Header por request: el HttpClient es compartido y no se muta
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_clientes.BaseUrl}/fichaje")
            {
                Content = JsonContent.Create(fichaje)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var cts = new CancellationTokenSource(Timeout);
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _authService.NotificarSesionExpirada();
                return new ResultadoFichaje(false, "Sesión expirada, iniciá sesión de nuevo");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return new ResultadoFichaje(false, "Tu usuario no tiene permiso para fichar");

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                return new ResultadoFichaje(true, data?.GetValueOrDefault("mensaje") ?? "Fichaje registrado correctamente");
            }

            var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
            var requiereMotivo = error?.GetValueOrDefault("codigo") == "MOTIVO_REQUERIDO";
            return new ResultadoFichaje(false, error?.GetValueOrDefault("mensaje") ?? "Error al fichar", requiereMotivo);
        }
        catch (OperationCanceledException)
        {
            // Sin reintento automático: el POST no es idempotente y un reintento podría duplicar el fichaje.
            return new ResultadoFichaje(false, "El servidor no respondió a tiempo. Revisá tu conexión y verificá si el fichaje quedó registrado.");
        }
        catch (Exception ex)
        {
            return new ResultadoFichaje(false, $"Error de conexión: {ex.Message}");
        }
    }
}
