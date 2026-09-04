using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

public partial class App : Application
{
    private readonly AuthService _authService;

    public App(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        MainPage = new AppShell();
    }

    protected override async void OnStart()
    {
        // Restaura el token guardado en SecureStorage y puebla el Singleton SessionManager
        var haySesion = await _authService.RestaurarSesionAsync();
        await Shell.Current.GoToAsync(haySesion ? "//FichajePage" : "//LoginPage");
    }
}
