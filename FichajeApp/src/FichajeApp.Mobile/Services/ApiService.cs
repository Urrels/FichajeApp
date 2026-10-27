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

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/fichaje", fichaje);

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
        catch (Exception ex)
        {
            return new ResultadoFichaje(false, $"Error de conexión: {ex.Message}");
        }
    }
}
