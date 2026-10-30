using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// No hay cámara frontal o no se pudo abrir: quien llama usa la cámara del sistema en su lugar
public class SelfieNoDisponibleException : Exception
{
    public SelfieNoDisponibleException(string mensaje, Exception? causa = null) : base(mensaje, causa) { }
}

// Cámara dentro de la app, abierta en la frontal. La cámara del sistema (MediaPicker) no permite elegir cuál abrir y
// cada marca de teléfono abre la suya; además, esta no saca a la app de primer plano (donde Android puede cerrarla).
public partial class SelfiePage : ContentPage
{
    private const int LadoMaximo = 640;
    private const int CalidadJpeg = 70;

    private readonly TaskCompletionSource<string?> _resultado = new();
    private bool _terminada;

    public SelfiePage() => InitializeComponent();

    // La foto en base64 (JPG), null si la persona canceló; lanza SelfieNoDisponibleException si no hay cámara frontal
    public Task<string?> Resultado => _resultado.Task;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var camaras = await Camara.GetAvailableCameras(CancellationToken.None);
            var frontal = camaras.FirstOrDefault(c => c.Position == CameraPosition.Front);
            if (frontal is null)
            {
                await Terminar(error: new SelfieNoDisponibleException("Este teléfono no tiene cámara frontal."));
                return;
            }
            Camara.SelectedCamera = frontal;
        }
        catch (Exception ex)
        {
            await Terminar(error: new SelfieNoDisponibleException("No se pudo abrir la cámara frontal.", ex));
        }
    }

    private async void OnSacar(object sender, EventArgs e)
    {
        Ocupar(true);
        try { await Camara.CaptureImage(CancellationToken.None); } // el resultado llega por MediaCaptured
        catch (Exception ex)
        {
            Ocupar(false);
            Aviso.Text = $"No se pudo sacar la foto: {ex.Message}";
        }
    }

    private async void OnFotoCapturada(object? sender, MediaCapturedEventArgs e)
    {
        try
        {
            // Se achica mientras se lee (ver ReductorDeImagenes): una foto entera agotaría la memoria
            var bytes = await ReductorDeImagenes.ReducirAsync(e.Media, LadoMaximo, permitirPng: false, calidadJpeg: CalidadJpeg, pesoMaximoPng: 0);
            await Terminar(base64: Convert.ToBase64String(bytes));
        }
        catch (Exception ex)
        {
            Ocupar(false);
            Aviso.Text = $"No se pudo procesar la foto: {ex.Message}. Probá de nuevo.";
        }
    }

    private void OnFotoFallida(object? sender, MediaCaptureFailedEventArgs e)
    {
        Ocupar(false);
        Aviso.Text = $"No se pudo sacar la foto: {e.FailureReason}. Probá de nuevo.";
    }

    private async void OnCancelar(object sender, EventArgs e) => await Terminar(base64: null);

    protected override bool OnBackButtonPressed()
    {
        _ = Terminar(base64: null);
        return true;
    }

    private void Ocupar(bool ocupado)
    {
        Ocupado.IsRunning = Ocupado.IsVisible = ocupado;
        SacarBoton.IsEnabled = !ocupado;
    }

    private async Task Terminar(string? base64 = null, Exception? error = null)
    {
        if (_terminada) return;
        _terminada = true;

        if (error is not null) _resultado.TrySetException(error);
        else _resultado.TrySetResult(base64);

        try { await Navigation.PopModalAsync(); } catch { /* ya se había cerrado */ }
    }
}
