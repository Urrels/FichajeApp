using System.Net.Http.Json;

namespace FichajeApp.Mobile.Services;

// Configuración de la conexión del servidor con Google Sheets (pantalla de configuración inicial).
// No usa sesión: se protege con la clave de instalación que define quien instala el servidor.
public class ConfiguracionApiService
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(60); // incluye crear las pestañas en la planilla

    private readonly HttpClient _http = new();
    private readonly IClienteService _clientes;

    public ConfiguracionApiService(IClienteService clientes) => _clientes = clientes;

    // true / false, o null si no se pudo consultar al servidor
    public async Task<bool?> ConfiguradaAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var r = await _http.GetFromJsonAsync<Dictionary<string, bool>>($"{_clientes.BaseUrl}/configuracion/estado", cts.Token);
            return r?.GetValueOrDefault("configurada");
        }
        catch { return null; }
    }

    public async Task<(bool Exito, string Mensaje)> GuardarAsync(object datos)
    {
        try
        {
            using var cts = new CancellationTokenSource(Espera);
            using var respuesta = await _http.PostAsJsonAsync($"{_clientes.BaseUrl}/configuracion/google", datos, cts.Token);

            string? mensaje = null;
            try
            {
                var json = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>(cts.Token);
                mensaje = json?.GetValueOrDefault("mensaje");
            }
            catch { /* respuesta sin JSON */ }

            if (respuesta.IsSuccessStatusCode) return (true, mensaje ?? "Conexión guardada.");
            if ((int)respuesta.StatusCode == 429) return (false, "Demasiados intentos. Esperá un minuto.");
            return (false, mensaje ?? $"El servidor respondió con error ({(int)respuesta.StatusCode})");
        }
        catch (OperationCanceledException) { return (false, "El servidor no respondió a tiempo."); }
        catch (Exception ex) { return (false, $"Error de conexión: {ex.Message}"); }
    }
}
