namespace FichajeApp.Mobile.Services;

// Patrón Singleton: una única instancia en memoria representa la sesión
// activa del dispositivo. Se accede como SessionManager.Instance desde
// cualquier parte de la app, sin pasar el usuario logueado por parámetros
// entre ViewModels/páginas.
//
// Además actúa como Subject del patrón Observer: cuando la sesión inicia
// o finaliza, notifica a todos los observers suscriptos (ej. FichajeViewModel).
public sealed class SessionManager
{
    private static readonly Lazy<SessionManager> _instancia = new(() => new SessionManager());
    public static SessionManager Instance => _instancia.Value;

    private readonly List<ISessionObserver> _observers = new();

    private SessionManager() { }

    public string? Token { get; private set; }
    public string? NombreCompleto { get; private set; }
    public int? EmpleadoId { get; private set; }
    public bool EstaLogueado => !string.IsNullOrEmpty(Token);

    public void Suscribir(ISessionObserver observer)
    {
        if (!_observers.Contains(observer))
            _observers.Add(observer);
    }

    public void Desuscribir(ISessionObserver observer)
    {
        _observers.Remove(observer);
    }

    public void IniciarSesion(string token, string nombreCompleto, int empleadoId)
    {
        Token = token;
        NombreCompleto = nombreCompleto;
        EmpleadoId = empleadoId;

        foreach (var observer in _observers)
            observer.OnSesionIniciada(nombreCompleto);
    }

    public void FinalizarSesion()
    {
        Token = null;
        NombreCompleto = null;
        EmpleadoId = null;

        foreach (var observer in _observers)
            observer.OnSesionFinalizada();
    }
}
