using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace FichajeApp.Mobile.Services;

// Un solo lugar para avisarle algo a la persona. Reglas:
//   - Salió bien           → snackbar corto abajo (3 s), sin botón: no interrumpe.
//   - Falló algo chico     → snackbar de error con "Reintentar".
//   - Hay que decidir      → ConfirmarAsync (modal) con el verbo de la acción en el botón.
//   - No se puede seguir   → pantalla de error (componente EstadoDeError), no un cartel.
//   - Dato mal escrito     → mensaje en línea bajo el campo (no pasa por acá).
public interface IFeedback
{
    Task ExitoAsync(string texto);
    Task AvisoAsync(string texto);
    Task ErrorAsync(string texto, Func<Task>? reintentar = null);
    Task<bool> ConfirmarAsync(string titulo, string texto, string confirmar);
}

public static class FeedbackExtensiones
{
    // El patrón más común: una acción que salió bien (snackbar verde) o mal (snackbar rojo con el motivo)
    public static Task ResultadoAsync(this IFeedback feedback, bool exito, string textoExito, string? textoError) =>
        exito ? feedback.ExitoAsync(textoExito) : feedback.ErrorAsync(TextosDeError.Para(null, textoError));
}

public class Feedback : IFeedback
{
    private static readonly TimeSpan Corto = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Largo = TimeSpan.FromSeconds(6);

    // Colores de estado fijos (no cambian con la marca del cliente): el verde siempre es éxito y el rojo siempre es error
    private static readonly Color Verde = Color.FromArgb("#2E7D32");
    private static readonly Color Ambar = Color.FromArgb("#9A5B00");
    private static readonly Color Rojo = Color.FromArgb("#C62828");

    public Task ExitoAsync(string texto) => MostrarAsync(texto, Verde, Corto);
    public Task AvisoAsync(string texto) => MostrarAsync(texto, Ambar, Largo);

    public Task ErrorAsync(string texto, Func<Task>? reintentar = null) =>
        MostrarAsync(texto, Rojo, Largo, reintentar is null ? null : () => _ = reintentar(), "Reintentar");

    public Task<bool> ConfirmarAsync(string titulo, string texto, string confirmar) =>
        MainThread.InvokeOnMainThreadAsync(() => Dialogos.Confirmar(titulo, texto, confirmar));

    private static Task MostrarAsync(string texto, Color fondo, TimeSpan duracion, Action? accion = null, string textoAccion = "OK")
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                var opciones = new SnackbarOptions
                {
                    BackgroundColor = fondo,
                    TextColor = Colors.White,
                    ActionButtonTextColor = Colors.White,
                    CornerRadius = new CornerRadius(12),
                    Font = Microsoft.Maui.Font.SystemFontOfSize(14)
                };
                await Snackbar.Make(texto, accion, textoAccion, duracion, opciones).Show();
            }
            catch
            {
                // Sin una pantalla donde mostrarlo (por ejemplo al cambiar de página): un cartel común, para no perder el aviso
                try { await Dialogos.Aviso(fondo == Rojo ? "No se pudo" : "Listo", texto); } catch { /* nada más que hacer */ }
            }
        });
    }
}
