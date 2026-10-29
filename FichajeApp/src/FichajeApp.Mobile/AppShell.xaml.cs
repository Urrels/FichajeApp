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
        Routing.RegisterRoute(nameof(RolesPage), typeof(RolesPage));
        Routing.RegisterRoute(nameof(BitacoraPage), typeof(BitacoraPage));
        Routing.RegisterRoute(nameof(FichajesAdminPage), typeof(FichajesAdminPage));
        Routing.RegisterRoute(nameof(LiquidacionPage), typeof(LiquidacionPage));
        Routing.RegisterRoute(nameof(CambiarPinPage), typeof(CambiarPinPage));

        SessionManager.Instance.Suscribir(this);
    }

    public void OnSesionIniciada(string nombreCompleto) => ActualizarMenu();

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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SupervisorItem.FlyoutItemIsVisible = supervisor;
            AdminItem.FlyoutItemIsVisible = administrador;
        });
    }
}
