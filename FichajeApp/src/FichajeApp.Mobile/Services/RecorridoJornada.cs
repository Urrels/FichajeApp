using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FichajeApp.Mobile.Services;

public class PuntoPendiente
{
    public DateTimeOffset FechaHora { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
}

// Los puntos esperan en un archivo del teléfono hasta que se pueden mandar (sin señal, app reiniciada, etc.)
// y se mandan de a lotes. Si el servidor dice "seguir: false" (jornada cerrada u opción apagada) se borra todo y se frena.
public static class RecorridoEnvio
{
    private const int MaxGuardados = 500;   // si pasa mucho tiempo sin señal, se descartan los más viejos
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Candado = new(1, 1);

    private static string Archivo => Path.Combine(FileSystem.AppDataDirectory, "recorrido-pendiente.json");

    public static bool HaySesion()
    {
        try { return !string.IsNullOrEmpty(SecureStorage.Default.GetAsync("auth_token").GetAwaiter().GetResult()); }
        catch { return false; }
    }

    private static List<PuntoPendiente> Leer()
    {
        try { return File.Exists(Archivo) ? JsonSerializer.Deserialize<List<PuntoPendiente>>(File.ReadAllText(Archivo)) ?? new() : new(); }
        catch { return new(); }
    }

    private static void Guardar(List<PuntoPendiente> puntos)
    {
        try { File.WriteAllText(Archivo, JsonSerializer.Serialize(puntos)); } catch { }
    }

    public static void Agregar(PuntoPendiente punto)
    {
        lock (Archivo)
        {
            var lista = Leer();
            lista.Add(punto);
            if (lista.Count > MaxGuardados) lista.RemoveRange(0, lista.Count - MaxGuardados);
            Guardar(lista);
        }
    }

    public static void Borrar()
    {
        lock (Archivo) { try { File.Delete(Archivo); } catch { } }
    }

    public static async Task EnviarPendientesAsync()
    {
        if (!await Candado.WaitAsync(0)) return;   // ya hay un envío en curso
        try
        {
            List<PuntoPendiente> lote;
            lock (Archivo) lote = Leer().Take(100).ToList();
            if (lote.Count == 0) return;

            var token = await SecureStorage.Default.GetAsync("auth_token");
            var clientes = IPlatformApplication.Current?.Services.GetService<IClienteService>();
            if (string.IsNullOrEmpty(token) || clientes is null) return;

            using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{clientes.BaseUrl}/fichaje/recorrido")
            {
                Content = JsonContent.Create(new { puntos = lote })
            };
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var respuesta = await Http.SendAsync(pedido);
            if (!respuesta.IsSuccessStatusCode) return;   // sin permiso o servidor caído: se reintenta con el próximo punto

            var datos = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var seguir = !datos.TryGetProperty("seguir", out var s) || s.ValueKind != JsonValueKind.False;

            if (!seguir)
            {
                Borrar();
                RecorridoJornada.Detener();
                return;
            }

            lock (Archivo)
            {
                var actuales = Leer();
                actuales.RemoveAll(p => lote.Any(l => l.FechaHora == p.FechaHora && l.Latitud == p.Latitud && l.Longitud == p.Longitud));
                Guardar(actuales);
            }
        }
        catch { /* sin conexión: queda guardado para el próximo intento */ }
        finally { Candado.Release(); }
    }
}

// Cuándo empieza y cuándo termina el registro del recorrido, y el aviso que la persona tiene que aceptar antes.
public static class RecorridoJornada
{
    private const string ClaveAceptado = "recorrido_aceptado";

    public static bool Activo => RecorridoServicioAndroid.Activo;

    // Aviso destacado: Play exige que se explique con claridad, antes de pedir el permiso, qué se registra y para qué
    private const string TextoAviso =
        "{0} va a registrar tu ubicación cada pocos minutos mientras tu jornada esté abierta: desde que fichás la entrada " +
        "hasta que fichás la salida, incluso con la app cerrada o sin usarla.\n\n" +
        "Tu empresa la usa para ver tu recorrido durante el trabajo. Vas a ver una notificación fija mientras se registra. " +
        "Se guarda 90 días. Fuera de la jornada no se registra nada.";

    // Al cambiar la jornada: Entrada empieza el registro (si la empresa lo activó), Salida lo termina
    public static async Task AlFicharAsync(string? tipo, ConfiguracionFichaje config)
    {
        // Fichar la entrada es un momento claro para preguntar, aunque haya dicho que no hace un rato
        if (tipo == "Entrada") await IniciarSiCorrespondeAsync(config, preguntarDeNuevo: true);
        else if (tipo == "Salida") Detener(borrarPendientes: false);
    }

    // Una sola pregunta a la vez (fichar y abrir la pantalla disparan el mismo aviso casi juntos) y, si la persona dijo que no,
    // no se le insiste cada vez que abre la pantalla: se espera este rato
    private static readonly SemaphoreSlim Pregunta = new(1, 1);
    private static DateTime _ultimaNegativa = DateTime.MinValue;
    private static readonly TimeSpan EsperaTrasNegativa = TimeSpan.FromMinutes(30);

    // Al abrir la app o la pantalla de fichar: se retoma si hay una jornada abierta y el servicio no estaba corriendo
    public static async Task RetomarAsync(ConfiguracionFichaje config, string? proximo)
    {
        if (proximo == "Salida" && config.SeguimientoRecorrido)
        {
            if (!Activo) await IniciarSiCorrespondeAsync(config);
        }
        else if (Activo) Detener(borrarPendientes: false);
        // Lo que quedó sin mandar se intenta de nuevo
        _ = RecorridoEnvio.EnviarPendientesAsync();
    }

    public static async Task IniciarSiCorrespondeAsync(ConfiguracionFichaje config, bool preguntarDeNuevo = false)
    {
        if (!await Pregunta.WaitAsync(0)) return;   // ya se está preguntando o iniciando
        try
        {
            if (!config.SeguimientoRecorrido || SessionManager.Instance.EsKiosco || Activo) return;

            if (!Preferences.Default.Get(ClaveAceptado, false))
            {
                if (!preguntarDeNuevo && DateTime.UtcNow - _ultimaNegativa < EsperaTrasNegativa) return;

                var nombre = IPlatformApplication.Current?.Services.GetService<IClienteService>()?.Nombre ?? "La app";
                // "Cancelar" no se guarda como un "no" definitivo: se vuelve a preguntar al fichar la próxima entrada
                if (!await Dialogos.Confirmar("Registro de tu recorrido", string.Format(TextoAviso, nombre), "Acepto"))
                {
                    _ultimaNegativa = DateTime.UtcNow;
                    return;
                }
                Preferences.Default.Set(ClaveAceptado, true);
            }

            var ubicacion = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (ubicacion != PermissionStatus.Granted) ubicacion = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (ubicacion != PermissionStatus.Granted) return;

            // Android 13 o más: sin este permiso la notificación fija no se ve (el registro funciona igual)
            var avisos = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (avisos != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.PostNotifications>();

            RecorridoServicioAndroid.Iniciar();
        }
        catch { /* el recorrido es un extra: nunca tiene que impedir fichar */ }
        finally { Pregunta.Release(); }
    }

    public static void Detener(bool borrarPendientes = false)
    {
        try { RecorridoServicioAndroid.Detener(); } catch { }
        if (borrarPendientes) RecorridoEnvio.Borrar();
    }
}
