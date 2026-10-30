namespace FichajeApp.Mobile.Services;

// Abstracciones para que el ViewModel no dependa de implementaciones concretas
// (Dependency Inversion) y se pueda probar con dobles.
public interface IGeolocationService
{
    Task<bool> PedirPermisoAsync();
    // precisionRequeridaMetros: lo que acepta el servidor; sirve para no esperar de más cuando la lectura ya alcanza
    Task<UbicacionResultado> ObtenerUbicacionAsync(double precisionRequeridaMetros);

    // Empieza a buscar señal al abrir la pantalla de fichar, así el GPS ya está enganchado al tocar el botón
    Task PrecalentarAsync(double precisionRequeridaMetros);

    // Texto de avance de la búsqueda ("Buscando señal GPS… ±45 m")
    Action<string>? Progreso { get; set; }
}

public interface IBiometricService
{
    Task<(bool exito, string mensaje)> AutenticarAsync();
}
