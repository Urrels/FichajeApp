using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

public partial class App : Application
{
    private readonly AuthService _authService;
    private readonly AparienciaService _apariencia;

    public App(AuthService authService, IClienteService clientes, AparienciaService apariencia)
    {
        InitializeComponent();
        clientes.AplicarTema(); // el cliente del build + la última apariencia conocida del servidor
        _authService = authService;
        _apariencia = apariencia;
        MainPage = new AppShell();
    }

    protected override async void OnStart()
    {
        // Restaura el token guardado en SecureStorage y puebla el Singleton SessionManager
        var haySesion = await _authService.RestaurarSesionAsync();
        await Shell.Current.GoToAsync(haySesion ? Navegacion.RutaInicial() : "//LoginPage");

        // La apariencia vigente se pide sin bloquear el arranque; si cambió, la pantalla se actualiza sola
        _ = _apariencia.ActualizarAsync();
    }
}
