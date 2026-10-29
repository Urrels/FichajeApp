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
        var destino = !haySesion ? "//LoginPage"
            : SessionManager.Instance.EsSupervisor ? "//SupervisorPage" : "//FichajePage";
        await Shell.Current.GoToAsync(destino);
    }
}
