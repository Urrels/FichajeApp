using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    private readonly AuthService _authService;

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
        await _viewModel.ActualizarProximoAsync();
        var sesion = SessionManager.Instance;
        PinButton.IsVisible = sesion.TienePermiso(NombresPermisos.CambiarPin);
        PanelButton.IsVisible = sesion.EsSupervisor || sesion.EsAdministrador;
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
