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
    private const string TextoListo = "Mirá a la cámara y tocá «Sacar foto»";

    private readonly TaskCompletionSource<string?> _resultado = new();
    private CameraView? _camara;
    private bool _terminada;

    public SelfiePage() => InitializeComponent();

    // La foto en base64 (JPG), null si la persona canceló; lanza SelfieNoDisponibleException si no hay cámara frontal
    public Task<string?> Resultado => _resultado.Task;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // Deja que se dibujen los botones y el aviso antes de arrancar la cámara (arrancarla bloquea la pantalla un rato)
            await Task.Delay(120);
            if (_terminada) return;

            _camara = new CameraView();
            _camara.MediaCaptured += OnFotoCapturada;
            _camara.MediaCaptureFailed += OnFotoFallida;
            Escena.Children.Insert(0, _camara);

            var camaras = await _camara.GetAvailableCameras(CancellationToken.None);
            var frontal = camaras.FirstOrDefault(c => c.Position == CameraPosition.Front);
            if (frontal is null)
            {
                await Terminar(error: new SelfieNoDisponibleException("Este teléfono no tiene cámara frontal."));
                return;
            }
            _camara.SelectedCamera = frontal;

            Aviso.Text = TextoListo;
            Ocupar(false);
        }
        catch (Exception ex)
        {
            await Terminar(error: new SelfieNoDisponibleException("No se pudo abrir la cámara frontal.", ex));
        }
    }

    // Soltar la cámara al salir: dejarla abierta al cerrar la pantalla es una de las causas de que la app se cierre
    protected override void OnDisappearing()
    {
        LiberarCamara();
        base.OnDisappearing();
    }

    private async void OnSacar(object sender, EventArgs e)
    {
        if (_camara is null || _terminada) return;

        Ocupar(true);
        try { await _camara.CaptureImage(CancellationToken.None); } // el resultado llega por MediaCaptured
        catch (Exception)
        {
            Reintentar("No pudimos sacar la foto. Probá de nuevo.");
        }
    }

    // MediaCaptured llega desde un hilo de la cámara, no el de la pantalla: tocar la pantalla (o cerrarla) desde ahí
    // cierra la app. Todo lo que sigue corre en el hilo principal y dentro de un try: es un async void.
    private async void OnFotoCapturada(object? sender, MediaCapturedEventArgs e)
    {
        try
        {
            // Se achica mientras se lee (ver ReductorDeImagenes): una foto entera agotaría la memoria
            var bytes = await ReductorDeImagenes.ReducirAsync(e.Media, LadoMaximo, permitirPng: false, calidadJpeg: CalidadJpeg, pesoMaximoPng: 0);
            var base64 = Convert.ToBase64String(bytes);
            await MainThread.InvokeOnMainThreadAsync(() => Terminar(base64: base64));
        }
        catch (Exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() => Reintentar("No pudimos procesar la foto. Probá de nuevo."));
        }
    }

    private void OnFotoFallida(object? sender, MediaCaptureFailedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => Reintentar("No pudimos sacar la foto. Probá de nuevo."));

    private async void OnCancelar(object sender, EventArgs e) => await Terminar(base64: null);

    protected override bool OnBackButtonPressed()
    {
        _ = Terminar(base64: null);
        return true;
    }

    private void Reintentar(string mensaje)
    {
        Ocupar(false);
        Aviso.Text = mensaje;
    }

    // ocupado = true mientras se saca o procesa la foto (y al abrir): el botón de sacar queda apagado
    private void Ocupar(bool ocupado)
    {
        Ocupado.IsRunning = Ocupado.IsVisible = ocupado;
        SacarBoton.IsEnabled = !ocupado && _camara is not null;
    }

    private void LiberarCamara()
    {
        var camara = _camara;
        if (camara is null) return;
        _camara = null;

        try
        {
            camara.MediaCaptured -= OnFotoCapturada;
            camara.MediaCaptureFailed -= OnFotoFallida;
            camara.StopCameraPreview();
            camara.Handler?.DisconnectHandler();
        }
        catch { /* ya estaba suelta */ }
    }

    private async Task Terminar(string? base64 = null, Exception? error = null)
    {
        if (_terminada) return;
        _terminada = true;

        LiberarCamara();
        if (error is not null) _resultado.TrySetException(error);
        else _resultado.TrySetResult(base64);

        try { await Navigation.PopModalAsync(); } catch { /* ya se había cerrado */ }
    }
}
