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
        catch (InvalidOperationException ex) // son mensajes pensados para la persona (GPS apagado, sin permiso, sin lectura)
        {
            return Fallo(ex.Message);
        }
        catch (Exception ex)
        {
            return Fallo($"Error de ubicación: {ex.Message}");
        }
    }

    // El GPS tarda en "enganchar": la primera lectura suele ser de pocos satélites (±100 m o peor). Se toman varias lecturas
    // seguidas durante unos segundos y se queda con la más precisa; si una ya alcanza, no se espera más.
    private const double PrecisionSuficienteMetros = 25;
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan EdadMaximaDeLaUltimaConocida = TimeSpan.FromMinutes(2);

    // Una sola búsqueda a la vez: dos pedidos de ubicación al mismo tiempo se pisan en Android y el segundo vuelve vacío
    // (por ejemplo, si la persona vuelve a fichar mientras el pedido anterior sigue buscando). El nuevo cancela al anterior.
    private static readonly SemaphoreSlim _unaBusqueda = new(1, 1);
    private static CancellationTokenSource? _busquedaActual;

    private sealed record Lectura(Location? Mejor, int Intentos, string? UltimoError);

    private static async Task<Location?> ObtenerLaMejorLecturaAsync()
    {
        _busquedaActual?.Cancel();
        var propia = new CancellationTokenSource(TiempoMaximo);
        _busquedaActual = propia;

        await _unaBusqueda.WaitAsync();
        try
        {
            var lectura = await BuscarAsync(propia.Token);
            if (lectura.Mejor is not null) return lectura.Mejor;

            // Última lectura que el teléfono ya tenía, si es reciente: mejor eso que dejar a la persona sin poder fichar
            // (el servidor igual verifica la precisión y la guarda en el fichaje)
            try
            {
                var ultima = await Geolocation.Default.GetLastKnownLocationAsync();
                if (ultima is not null && DateTimeOffset.UtcNow - ultima.Timestamp <= EdadMaximaDeLaUltimaConocida)
                    return ultima;
            }
            catch { /* sin última conocida */ }

            throw new InvalidOperationException(
                $"No se pudo obtener la ubicación (intentos: {lectura.Intentos}{(lectura.UltimoError is null ? "" : ", último error: " + lectura.UltimoError)}). " +
                "Revisá que el GPS esté activado y probá al aire libre.");
        }
        finally
        {
            _unaBusqueda.Release();
            if (ReferenceEquals(_busquedaActual, propia)) _busquedaActual = null;
        }
    }

    private static async Task<Lectura> BuscarAsync(CancellationToken cancelar)
    {
        Location? mejor = null;
        int intentos = 0;
        string? ultimoError = null;

        while (!cancelar.IsCancellationRequested)
        {
            intentos++;
            try
            {
                // Después de un intento fallido se baja la exigencia: la red también da una posición aproximada
                var precision = intentos <= 2 ? GeolocationAccuracy.Best : GeolocationAccuracy.Medium;
                var lectura = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(precision, TimeSpan.FromSeconds(6)), cancelar);

                if (lectura is not null && (mejor is null || (lectura.Accuracy ?? double.MaxValue) < (mejor.Accuracy ?? double.MaxValue)))
                    mejor = lectura;

                if (mejor?.Accuracy is <= PrecisionSuficienteMetros) break;
            }
            catch (FeatureNotEnabledException) { throw new InvalidOperationException("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar."); }
            catch (PermissionException) { throw new InvalidOperationException("Falta el permiso de ubicación. Habilitalo en Ajustes → Apps → Fichaje → Permisos."); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                ultimoError = ex.GetType().Name; // un intento fallido no cancela el resto
                try { await Task.Delay(500, cancelar); } catch (OperationCanceledException) { break; }
            }
        }
        return new Lectura(mejor, intentos, ultimoError);
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
