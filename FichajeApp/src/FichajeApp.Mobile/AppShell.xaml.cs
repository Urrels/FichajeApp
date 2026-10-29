using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

// Observer de la sesión: muestra el ítem "Supervisor" del menú solo si el usuario lo es.
public partial class AppShell : Shell, ISessionObserver
{
    public AppShell()
    {
        InitializeComponent();
        SessionManager.Instance.Suscribir(this);
    }

    public void OnSesionIniciada(string nombreCompleto) => ActualizarMenu();

    public void OnSesionFinalizada() => ActualizarMenu();

    private void ActualizarMenu()
    {
        var esSupervisor = SessionManager.Instance.EsSupervisor;
        MainThread.BeginInvokeOnMainThread(() => SupervisorItem.FlyoutItemIsVisible = esSupervisor);
    }
}
