namespace FichajeApp.Mobile.Services;

public record UbicacionResultado(
    bool Exito, double Lat, double Lon, double? Precision, bool EsFalsa, string? Direccion, string Mensaje);

public class GeolocationService : IGeolocationService
{
    // Se pide el permiso antes y por separado, para no superponer su diálogo con otros del sistema.
    public async Task<bool> PedirPermisoAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    // Se pide la ubicación solo al fichar: la app no rastrea en segundo plano.
    public async Task<UbicacionResultado> ObtenerUbicacionAsync()
    {
        try
        {
            if (!await PedirPermisoAsync())
                return Fallo("Permiso de ubicación denegado");

            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request);

            if (location is null)
                return Fallo("No se pudo obtener la ubicación");

            var direccion = await ObtenerDireccionAsync(location.Latitude, location.Longitude);

            return new UbicacionResultado(true, location.Latitude, location.Longitude,
                location.Accuracy, location.IsFromMockProvider, direccion, "OK");
        }
        catch (Exception ex)
        {
            return Fallo($"Error de ubicación: {ex.Message}");
        }
    }

    // La dirección es opcional: si falla o tarda más de 3 s, el fichaje sigue sin ella.
    private static async Task<string?> ObtenerDireccionAsync(double lat, double lon)
    {
        try
        {
            var geocoding = Geocoding.Default.GetPlacemarksAsync(lat, lon);
            if (await Task.WhenAny(geocoding, Task.Delay(TimeSpan.FromSeconds(3))) != geocoding)
                return null;
            var placemarks = await geocoding;
            var p = placemarks?.FirstOrDefault();
            if (p is null) return null;

            var calle = string.Join(" ", new[] { p.Thoroughfare, p.SubThoroughfare }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var partes = new[] { calle, p.Locality }.Where(s => !string.IsNullOrWhiteSpace(s));
            var texto = string.Join(", ", partes);
            return texto.Length == 0 ? null : texto;
        }
        catch
        {
            return null;
        }
    }

    private static UbicacionResultado Fallo(string mensaje) =>
        new(false, 0, 0, null, false, null, mensaje);
}
