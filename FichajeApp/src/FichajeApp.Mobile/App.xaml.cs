using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

public partial class App : Application
{
    private readonly AuthService _authService;
    private readonly AparienciaService _apariencia;

    private readonly PerfilFotos _perfiles;

    public App(AuthService authService, IClienteService clientes, AparienciaService apariencia, PerfilFotos perfiles)
    {
        _perfiles = perfiles;
        InitializeComponent();

        // El diseño es claro (fondo y tarjetas claras). Con el teléfono en modo oscuro, los textos de la plantilla de MAUI
        // pasaban a blanco y se leían muy mal sobre ese fondo.
        UserAppTheme = AppTheme.Light;

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

        // Sesión restaurada sin foto de perfil (por ejemplo, quien ya estaba dado de alta antes de esta versión)
        if (haySesion)
        {
            await FotoDePerfil.ExigirSiFaltaAsync(_perfiles, _authService);
            if (!SessionManager.Instance.EstaLogueado) await Shell.Current.GoToAsync("//LoginPage");
            else _ = PushDispositivo.PrepararAsync();
        }
    }
}
