using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    private readonly ConfiguracionApiService _configuracion;
    private bool _yaOfrecioConfigurar;

    public LoginPage(AuthService authService, ConfiguracionApiService configuracion)
    {
        InitializeComponent();
        _configuracion = configuracion;
        _viewModel = new LoginViewModel(authService);
        _viewModel.LoginExitoso += OnLoginExitoso;
        BindingContext = _viewModel;
    }

    // Servidor recién instalado: lleva directo a conectar la planilla (una sola vez por apertura de la app)
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_yaOfrecioConfigurar) return;
        _yaOfrecioConfigurar = true;

        if (await _configuracion.ConfiguradaAsync() == false)
            await Shell.Current.GoToAsync(nameof(ConfiguracionPage));
    }

    private async void OnConfigurarClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ConfiguracionPage));

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await _viewModel.LoginAsync();
    }

    private async void OnLoginExitoso()
    {
        // El supervisor y el administrador entran directo a su panel; desde el menú pueden ir a fichar
        await Shell.Current.GoToAsync(Navegacion.RutaInicial());
    }
}
