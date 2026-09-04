namespace FichajeApp.Mobile.Services;

public class GeolocationService
{
    public async Task<(bool exito, double lat, double lon, string mensaje)> ObtenerUbicacionAsync()
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                return (false, 0, 0, "Permiso de ubicación denegado");

            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request);

            if (location is null)
                return (false, 0, 0, "No se pudo obtener la ubicación");

            return (true, location.Latitude, location.Longitude, "OK");
        }
        catch (Exception ex)
        {
            return (false, 0, 0, $"Error de ubicación: {ex.Message}");
        }
    }
}
