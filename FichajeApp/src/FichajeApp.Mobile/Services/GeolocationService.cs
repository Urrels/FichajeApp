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

            var location = await ObtenerLaMejorLecturaAsync();

            if (location is null)
                return Fallo("No se pudo obtener la ubicación. Activá el GPS del teléfono y probá al aire libre.");

            var direccion = await ObtenerDireccionAsync(location.Latitude, location.Longitude);

            return new UbicacionResultado(true, location.Latitude, location.Longitude,
                location.Accuracy, location.IsFromMockProvider, direccion, "OK");
        }
        catch (Exception ex)
        {
            return Fallo($"Error de ubicación: {ex.Message}");
        }
    }

    // El GPS tarda en "enganchar": la primera lectura suele ser de pocos satélites (±100 m o peor). Se toman varias lecturas
    // seguidas durante unos segundos y se queda con la más precisa; si una ya alcanza, no se espera más.
    private const double PrecisionSuficienteMetros = 25;
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(15);

    private static async Task<Location?> ObtenerLaMejorLecturaAsync()
    {
        Location? mejor = null;
        var inicio = DateTime.UtcNow;

        while (DateTime.UtcNow - inicio < TiempoMaximo)
        {
            try
            {
                var lectura = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5)));

                if (lectura is not null && (mejor is null || (lectura.Accuracy ?? double.MaxValue) < (mejor.Accuracy ?? double.MaxValue)))
                    mejor = lectura;

                if (mejor?.Accuracy is <= PrecisionSuficienteMetros) break;
            }
            catch (FeatureNotEnabledException) { throw; }   // GPS apagado: no tiene sentido seguir intentando
            catch (PermissionException) { throw; }
            catch { /* una lectura fallida no cancela el resto */ }
        }
        return mejor;
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
