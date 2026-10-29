namespace FichajeApp.Mobile.Services;

// Abstracciones para que el ViewModel no dependa de implementaciones concretas
// (Dependency Inversion) y se pueda probar con dobles.
public interface IGeolocationService
{
    Task<bool> PedirPermisoAsync();
    Task<UbicacionResultado> ObtenerUbicacionAsync();
}

public interface IBiometricService
{
    Task<(bool exito, string mensaje)> AutenticarAsync();
}
