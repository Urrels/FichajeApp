using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.Views;

namespace FichajeApp.Mobile;

// Observer de la sesión: muestra en el menú solo lo que el usuario tiene permiso de ver.
public partial class AppShell : Shell, ISessionObserver
{
    public AppShell()
    {
        InitializeComponent();

        // Pantallas que se abren desde el menú de administración
        Routing.RegisterRoute(nameof(UsuariosPage), typeof(UsuariosPage));
        Routing.RegisterRoute(nameof(UsuarioNuevoPage), typeof(UsuarioNuevoPage));
        Routing.RegisterRoute(nameof(HorarioPage), typeof(HorarioPage));
        Routing.RegisterRoute(nameof(RecorridoPage), typeof(RecorridoPage));
        Routing.RegisterRoute(nameof(NotificacionesPage), typeof(NotificacionesPage));
        Routing.RegisterRoute(nameof(OlvidePinPage), typeof(OlvidePinPage));
        Routing.RegisterRoute(nameof(SitiosPage), typeof(SitiosPage));
        Routing.RegisterRoute(nameof(RolesPage), typeof(RolesPage));
        Routing.RegisterRoute(nameof(BitacoraPage), typeof(BitacoraPage));
        Routing.RegisterRoute(nameof(FichajesAdminPage), typeof(FichajesAdminPage));
        Routing.RegisterRoute(nameof(LiquidacionPage), typeof(LiquidacionPage));
        Routing.RegisterRoute(nameof(CambiarPinPage), typeof(CambiarPinPage));
        Routing.RegisterRoute(nameof(AparienciaPage), typeof(AparienciaPage));
        Routing.RegisterRoute(nameof(ConfiguracionPage), typeof(ConfiguracionPage));
        Routing.RegisterRoute(nameof(AyudaClavePage), typeof(AyudaClavePage));
        Routing.RegisterRoute(nameof(ConfigFichajePage), typeof(ConfigFichajePage));
        Routing.RegisterRoute(nameof(FotoFichajePage), typeof(FotoFichajePage));

        SessionManager.Instance.Suscribir(this);
    }

    public void OnSesionIniciada(string nombreCompleto)
    {
        ActualizarMenu();
        _ = ActualizarGloboAsync();
    }

    private async void OnNotificaciones(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(NotificacionesPage));
    }

    // "Notificaciones (2)": cuantas hay sin leer. Si no se puede consultar, queda sin numero.
    public async Task ActualizarGloboAsync()
    {
        int cantidad = 0;
        try
        {
            var api = IPlatformApplication.Current?.Services.GetService<AdminApiService>();
            if (api is not null && SessionManager.Instance.EstaLogueado)
            {
                var r = await api.CantidadDeNotificaciones();
                if (r.Exito && r.Datos is not null) cantidad = r.Datos.NoLeidas;
            }
        }
        catch { }
        MainThread.BeginInvokeOnMainThread(() => NotifItem.Text = cantidad > 0 ? $"Notificaciones ({cantidad})" : "Notificaciones");
    }

    // Al cerrar la sesión (o vencer el token) se vuelve al login desde cualquier pantalla
    public void OnSesionFinalizada()
    {
        ActualizarMenu();
        MainThread.BeginInvokeOnMainThread(async () => await GoToAsync("//LoginPage"));
    }

    private void ActualizarMenu()
    {
        var sesion = SessionManager.Instance;
        bool supervisor = sesion.EsSupervisor;
        bool administrador = sesion.EsAdministrador;
        bool kiosco = sesion.EsKiosco;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SupervisorItem.FlyoutItemIsVisible = supervisor;
            AdminItem.FlyoutItemIsVisible = administrador;
            FichajeItem.FlyoutItemIsVisible = !kiosco;
            Shell.SetFlyoutItemIsVisible(NotifItem, sesion.EstaLogueado && !kiosco);
        });
    }
}
