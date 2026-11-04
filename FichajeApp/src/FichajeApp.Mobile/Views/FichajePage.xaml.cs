using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    private readonly AuthService _authService;
    private bool _visible;
    private static readonly System.Globalization.CultureInfo Es = new("es-AR");

    public FichajePage(FichajeViewModel viewModel, AuthService authService)
    {
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
            await Halo.ScaleTo(1.08, 1400, Easing.SinInOut);
            await Halo.ScaleTo(1.0, 1400, Easing.SinInOut);
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
