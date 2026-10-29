using System.Net.Http.Json;
using System.Text.Json;

namespace FichajeApp.Mobile.Services;

public record ColoresCliente(string Primario, string PrimarioTexto, string Acento);

public record ClienteConfig(string Nombre, string ApiBaseUrl, ColoresCliente Colores);

// Lo que el administrador definió desde la app y publica el servidor (GET /api/apariencia).
// Un campo nulo significa "usar el del cliente" (el que viene en el build).
public record AparienciaRemota(
    string? Nombre, string? ColorPrimario, string? ColorPrimarioTexto, string? ColorAcento,
    bool TieneLogo, string? VersionLogo);

// Los valores que se están usando ahora (los del servidor encima de los del build)
public record AparienciaActual(string Nombre, string Primario, string PrimarioTexto, string Acento, bool TieneLogo);

// Identidad del cliente al que pertenece este build de la app (URL de la API, nombre y colores).
// La config viene de Clientes/<cliente>/cliente.json, embebida al compilar con -p:Cliente=<cliente>.
// Encima de eso, el administrador puede cambiar colores, nombre y logo desde la app (no el ícono del
// launcher: ese lo fija Android al instalar).
public interface IClienteService
{
    string Nombre { get; }
    string BaseUrl { get; }

    // Dirección de la API que se escribió a mano en el teléfono (ajuste oculto del login); null = la del build
    string? ServidorPersonalizado { get; }
    void CambiarServidor(string? url);

    AparienciaActual Actual { get; }

    // Aplica el cliente + la última apariencia conocida del servidor
    void AplicarTema();

    // Aplica la apariencia del servidor y la recuerda para el próximo arranque.
    // Con soloVistaPrevia no se recuerda: sirve para ver cómo queda antes de guardar.
    void AplicarApariencia(AparienciaRemota? remota, bool soloVistaPrevia = false);
}

public class ClienteService : IClienteService
{
    private const string ClaveCache = "apariencia_remota";
    private const string ClaveServidor = "servidor_personalizado";

    private readonly ClienteConfig _config;
    private AparienciaRemota? _remota; // la guardada en el servidor (la última que se conoce)

    public ClienteService()
    {
        using var stream = typeof(ClienteService).Assembly.GetManifestResourceStream("cliente.json")
            ?? throw new InvalidOperationException(
                "Falta cliente.json embebido. Ver FichajeApp.Mobile.Clientes.csproj.snippet.txt.");

        _config = JsonSerializer.Deserialize<ClienteConfig>(stream,
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? throw new InvalidOperationException("cliente.json inválido.");

        _remota = LeerCache();
        ServidorPersonalizado = LeerServidor();
    }

    public string Nombre => Actual.Nombre;
    public string BaseUrl => ServidorPersonalizado ?? _config.ApiBaseUrl;

    public string? ServidorPersonalizado { get; private set; }

    // Un mismo APK sirve para cualquier servidor: la dirección se guarda en el teléfono. La apariencia recordada era de
    // otro servidor, así que se descarta hasta que se vuelva a pedir.
    public void CambiarServidor(string? url)
    {
        ServidorPersonalizado = string.IsNullOrWhiteSpace(url) ? null : url;
        try
        {
            if (ServidorPersonalizado is null) Preferences.Default.Remove(ClaveServidor);
            else Preferences.Default.Set(ClaveServidor, ServidorPersonalizado);
        }
        catch { /* sin preferencias: vale hasta cerrar la app */ }

        _remota = null;
        GuardarCache(null);
        AplicarTema();
    }

    private static string? LeerServidor()
    {
        try
        {
            var guardado = Preferences.Default.Get(ClaveServidor, string.Empty);
            return string.IsNullOrWhiteSpace(guardado) ? null : guardado;
        }
        catch { return null; }
    }

    public AparienciaActual Actual => Combinar(_remota);

    public void AplicarTema() => Aplicar(Combinar(_remota), _remota);

    public void AplicarApariencia(AparienciaRemota? remota, bool soloVistaPrevia = false)
    {
        if (!soloVistaPrevia)
        {
            _remota = remota;
            GuardarCache(remota);
        }
        Aplicar(Combinar(remota), remota);
    }

    private AparienciaActual Combinar(AparienciaRemota? r) => new(
        string.IsNullOrWhiteSpace(r?.Nombre) ? _config.Nombre : r!.Nombre!,
        ColorValido(r?.ColorPrimario) ?? _config.Colores.Primario,
        ColorValido(r?.ColorPrimarioTexto) ?? _config.Colores.PrimarioTexto,
        ColorValido(r?.ColorAcento) ?? _config.Colores.Acento,
        r?.TieneLogo == true);

    // Un color que no se pueda interpretar no tiene que romper la app: se usa el del cliente
    private static string? ColorValido(string? color) =>
        !string.IsNullOrWhiteSpace(color) && Color.TryParse(color, out _) ? color : null;

    // Pisa los colores base de Resources/Styles/Tema.xaml. Las pantallas usan DynamicResource,
    // así que se actualizan solas, y también los botones (ver el estilo de Button en Tema.xaml).
    private void Aplicar(AparienciaActual actual, AparienciaRemota? remota)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => Aplicar(actual, remota));
            return;
        }

        var recursos = Application.Current?.Resources;
        if (recursos is null) return;

        var primario = Color.FromArgb(actual.Primario);
        recursos["NombreCliente"] = actual.Nombre;
        recursos["ColorPrimario"] = primario;
        recursos["ColorPrimarioTexto"] = Color.FromArgb(actual.PrimarioTexto);
        recursos["ColorAcento"] = Color.FromArgb(actual.Acento);
        recursos["ColorPrimarioOscuro"] = Oscurecer(primario, 0.25f);

        // El logo del servidor lleva ?v=<versión>: si cambia la imagen, cambia la versión y se baja de nuevo
        recursos["LogoCliente"] = actual.TieneLogo && !string.IsNullOrEmpty(remota?.VersionLogo)
            ? ImageSource.FromUri(new Uri($"{BaseUrl}/apariencia/logo?v={remota.VersionLogo}"))
            : ImageSource.FromFile("logo");

        AplicarBarraDeEstado(Oscurecer(primario, 0.25f));
    }

    private static Color Oscurecer(Color color, float cuanto) =>
        new(color.Red * (1 - cuanto), color.Green * (1 - cuanto), color.Blue * (1 - cuanto));

    // La barra de arriba (hora y batería) toma el color principal, un poco más oscuro
    private static void AplicarBarraDeEstado(Color color)
    {
#if ANDROID
        try
        {
            var ventana = Platform.CurrentActivity?.Window;
            ventana?.SetStatusBarColor(Microsoft.Maui.Platform.ColorExtensions.ToPlatform(color));
        }
        catch
        {
            // Es un detalle estético: si falla (por ejemplo, todavía no hay Activity), no se avisa
        }
#endif
    }

    private static AparienciaRemota? LeerCache()
    {
        try
        {
            var json = Preferences.Default.Get<string?>(ClaveCache, null);
            return string.IsNullOrEmpty(json)
                ? null
                : JsonSerializer.Deserialize<AparienciaRemota>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static void GuardarCache(AparienciaRemota? remota)
    {
        try
        {
            if (remota is null) Preferences.Default.Remove(ClaveCache);
            else Preferences.Default.Set(ClaveCache, JsonSerializer.Serialize(remota));
        }
        catch { /* sin caché el próximo arranque usa los del cliente hasta consultar al servidor */ }
    }
}

// Consulta la apariencia publicada por el servidor. Se pide antes de iniciar sesión (es la marca de la empresa),
// con poca espera: si no hay conexión se sigue con la última conocida.
public class AparienciaService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly IClienteService _clientes;

    public AparienciaService(IClienteService clientes) => _clientes = clientes;

    public async Task ActualizarAsync()
    {
        try
        {
            using var respuesta = await Http.GetAsync($"{_clientes.BaseUrl}/apariencia");
            if (!respuesta.IsSuccessStatusCode) return;

            var remota = await respuesta.Content.ReadFromJsonAsync<AparienciaRemota>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _clientes.AplicarApariencia(remota);
        }
        catch
        {
            // Sin conexión o servidor caído: se queda con la última apariencia conocida
        }
    }
}
