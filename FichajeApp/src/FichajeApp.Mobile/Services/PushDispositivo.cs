using System.Net.Http.Headers;
using System.Net.Http.Json;
using Android.Gms.Extensions;
using Firebase.Messaging;

namespace FichajeApp.Mobile.Services;

// Notificaciones push: le avisa al servidor el token de Firebase de este teléfono para que le mande los avisos de la cuenta.
// Si el proyecto de Firebase todavía no está configurado en la app (falta google-services.json), nada de esto hace efecto y la app funciona igual.
public static class PushDispositivo
{
    public const string ClaveToken = "push_token";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Después de iniciar sesión: pide el permiso de notificaciones (Android 13 o más) y registra el token
    public static async Task PrepararAsync()
    {
        try
        {
            var estado = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (estado != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch { }
        await RegistrarAsync();
    }

    public static async Task RegistrarAsync()
    {
        try
        {
            var token = await TokenAsync();
            if (string.IsNullOrEmpty(token)) return;
            await PostAsync("notificaciones/dispositivo", token, SessionManager.Instance.Token);
        }
        catch { /* sin Firebase o sin conexión: se vuelve a intentar en el próximo inicio */ }
    }

    // Al cerrar sesión: este teléfono deja de recibir los avisos de la cuenta. Se llama ANTES de borrar la sesión.
    public static Task QuitarAsync()
    {
        var token = Preferences.Default.Get(ClaveToken, "");
        var sesion = SessionManager.Instance.Token;   // se lee ahora, antes de que se borre
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sesion)) return Task.CompletedTask;
        return PostAsync("notificaciones/dispositivo/quitar", token, sesion);
    }

    private static async Task<string?> TokenAsync()
    {
        var guardado = Preferences.Default.Get(ClaveToken, "");
        try
        {
            var actual = (await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>())?.ToString();
            if (!string.IsNullOrEmpty(actual))
            {
                Preferences.Default.Set(ClaveToken, actual);
                return actual;
            }
        }
        catch { /* Firebase no inicializado: falta google-services.json */ }
        return string.IsNullOrEmpty(guardado) ? null : guardado;
    }

    private static async Task PostAsync(string ruta, string token, string? sesion)
    {
        try
        {
            var clientes = IPlatformApplication.Current?.Services.GetService<IClienteService>();
            if (clientes is null || string.IsNullOrEmpty(sesion)) return;

            using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{clientes.BaseUrl}/{ruta}") { Content = JsonContent.Create(new { token }) };
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sesion);
            using var _ = await Http.SendAsync(pedido);
        }
        catch { }
    }
}
