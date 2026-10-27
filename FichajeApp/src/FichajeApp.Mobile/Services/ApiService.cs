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

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private const string BaseUrl = "https://localhost:7001/api";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public ApiService(AuthService authService)
    {
        _httpClient = new HttpClient();
        _authService = authService;
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/fichaje")
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
