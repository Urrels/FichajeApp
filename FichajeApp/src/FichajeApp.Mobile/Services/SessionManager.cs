namespace FichajeApp.Mobile.Services;

// Patrón Singleton: una única instancia en memoria representa la sesión
// activa del dispositivo. Se accede como SessionManager.Instance desde
// cualquier parte de la app, sin pasar el usuario logueado por parámetros
// entre ViewModels/páginas.
//
// Además actúa como Subject del patrón Observer: cuando la sesión inicia
// o finaliza, notifica a todos los observers suscriptos (ej. FichajeViewModel).
//
// Los permisos vienen del servidor (en el login) y son los que decide qué ve cada usuario:
// igual que en ingenieria_software, se consulta con TienePermiso y nunca por el nombre del rol.
public sealed class SessionManager
{
    private static readonly Lazy<SessionManager> _instancia = new(() => new SessionManager());
    public static SessionManager Instance => _instancia.Value;

    private readonly List<ISessionObserver> _observers = new();
    private HashSet<string> _permisos = new(StringComparer.OrdinalIgnoreCase);

    private SessionManager() { }

    public string? Token { get; private set; }
    public string? NombreCompleto { get; private set; }
    public int? EmpleadoId { get; private set; }
    public string Rol { get; private set; } = "Empleado"; // rol principal, solo para mostrar
    public bool EstaLogueado => !string.IsNullOrEmpty(Token);

    public bool TienePermiso(string permiso) => _permisos.Contains(permiso);
    public bool TieneAlgunPermiso(IEnumerable<string> permisos) => permisos.Any(_permisos.Contains);
    public bool EsSupervisor => TienePermiso(NombresPermisos.VerPanelSupervisor);
    public bool EsAdministrador => TieneAlgunPermiso(NombresPermisos.DeAdministracion);

    public void Suscribir(ISessionObserver observer)
    {
        if (!_observers.Contains(observer))
            _observers.Add(observer);
    }

    public void Desuscribir(ISessionObserver observer)
    {
        _observers.Remove(observer);
    }

    public void IniciarSesion(string token, string nombreCompleto, int empleadoId, string rol, IEnumerable<string> permisos)
    {
        Token = token;
        NombreCompleto = nombreCompleto;
        EmpleadoId = empleadoId;
        Rol = rol;
        _permisos = new HashSet<string>(permisos, StringComparer.OrdinalIgnoreCase);

        foreach (var observer in _observers)
            observer.OnSesionIniciada(nombreCompleto);
    }

    public void FinalizarSesion()
    {
        Token = null;
        NombreCompleto = null;
        EmpleadoId = null;
        Rol = "Empleado";
        _permisos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var observer in _observers)
            observer.OnSesionFinalizada();
    }
}
