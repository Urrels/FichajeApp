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
        // El supervisor entra directo a su panel; desde el menú puede ir a fichar
        await Shell.Current.GoToAsync(SessionManager.Instance.EsSupervisor ? "//SupervisorPage" : "//FichajePage");
    }
}
