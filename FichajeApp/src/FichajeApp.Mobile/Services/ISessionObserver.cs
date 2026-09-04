namespace FichajeApp.Mobile.Services;

// Patrón Observer: cualquier ViewModel/página se puede suscribir a cambios
// de sesión (login/logout/expiración) sin acoplarse directamente a SessionManager.
public interface ISessionObserver
{
    void OnSesionIniciada(string nombreCompleto);
    void OnSesionFinalizada();
}
