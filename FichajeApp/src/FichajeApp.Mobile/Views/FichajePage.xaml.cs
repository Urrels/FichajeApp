using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    private readonly AuthService _authService;
    private bool _visible;
    private static readonly System.Globalization.CultureInfo Es = new("es-AR");

    private readonly PerfilFotos _perfiles;
    private readonly IFeedback _feedback;

    public FichajePage(FichajeViewModel viewModel, AuthService authService, PerfilFotos perfiles, IFeedback feedback)
    {
        _perfiles = perfiles;
        _feedback = feedback;
        InitializeComponent();
        _authService = authService;
        _viewModel = viewModel;
        _viewModel.SesionFinalizada += OnSesionFinalizada;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        ActualizarReloj();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () => { ActualizarReloj(); return _visible; });
        _ = RespirarAsync();
        await _viewModel.ActualizarProximoAsync();
        _ = _viewModel.PrepararUbicacionAsync(); // sin esperar: el GPS engancha mientras la persona se prepara
        var sesion = SessionManager.Instance;
        MiFoto.Nombre = sesion.NombreCompleto ?? string.Empty;
        MiFoto.EmpleadoId = sesion.EmpleadoId ?? 0;
        PinButton.IsVisible = sesion.TienePermiso(NombresPermisos.CambiarPin);
        PanelButton.IsVisible = sesion.EsSupervisor || sesion.EsAdministrador;
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        base.OnDisappearing();
    }

    private void ActualizarReloj()
    {
        var ahora = DateTime.Now;
        HoraLabel.Text = ahora.ToString("HH:mm", Es);
        var fecha = ahora.ToString("dddd d 'de' MMMM", Es);
        FechaLabel.Text = char.ToUpper(fecha[0], Es) + fecha[1..];
    }

    // El halo se agranda y achica despacio: invita a tocar sin distraer
    private async Task RespirarAsync()
    {
        while (_visible)
        {
            // Con las animaciones del sistema apagadas ScaleTo termina al instante: el Delay evita un bucle sin pausa
            await Task.WhenAll(Halo.ScaleTo(1.08, 1400, Easing.SinInOut), Task.Delay(1400));
            await Task.WhenAll(Halo.ScaleTo(1.0, 1400, Easing.SinInOut), Task.Delay(1400));
        }
    }

    // Tocar la propia foto: sacar una nueva con la cámara frontal o quitarla (vuelven a verse las iniciales)
    private async void OnMiFotoTapped(object? sender, TappedEventArgs e)
    {
        const string Nueva = "Sacar una foto nueva", Quitar = "Quitar mi foto";
        var elegida = await Dialogos.Elegir("Tu foto de perfil", new[] { Nueva, Quitar });
        if (elegida is null) return;

        try
        {
            if (elegida == Nueva)
            {
                var foto = await FotoFichaje.TomarAsync();
                if (foto is null) return;   // canceló
                var r = await _perfiles.GuardarAsync(null, foto);
                await _feedback.ResultadoAsync(r.Exito, "Foto de perfil actualizada.", r.Mensaje);
            }
            else
            {
                var r = await _perfiles.QuitarAsync(null);
                await _feedback.ResultadoAsync(r.Exito, "Foto de perfil quitada.", r.Mensaje);
            }
        }
        catch (Exception ex)
        {
            await Dialogos.Aviso("No se pudo", TextosDeError.DeExcepcion(ex));
        }
    }

    private async void OnVolverAlPanel(object sender, EventArgs e) => await Shell.Current.GoToAsync(Navegacion.RutaInicial());

    private async void OnCambiarPin(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(CambiarPinPage));

    private void OnCerrarSesion(object sender, EventArgs e) => _authService.Logout();

    private async void OnFicharClicked(object sender, EventArgs e)
    {
        await _viewModel.FicharAsync();
    }

    private async void OnAbrirMapsClicked(object sender, EventArgs e)
    {
        await _viewModel.AbrirEnMapsAsync();
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
