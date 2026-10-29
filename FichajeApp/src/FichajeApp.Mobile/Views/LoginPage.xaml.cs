using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(AuthService authService)
    {
        InitializeComponent();
        _viewModel = new LoginViewModel(authService);
        _viewModel.LoginExitoso += OnLoginExitoso;
        BindingContext = _viewModel;
    }

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
