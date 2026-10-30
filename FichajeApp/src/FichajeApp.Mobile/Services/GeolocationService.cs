namespace FichajeApp.Mobile.Services;

public record UbicacionResultado(
    bool Exito, double Lat, double Lon, double? Precision, bool EsFalsa, string? Direccion, string Mensaje);

// Ubicación para fichar. Cómo se consigue una buena posición con un teléfono:
//  - El GPS necesita unos segundos para "enganchar" satélites y la precisión mejora con el tiempo (la primera lectura suele
//    ser de ±100 m o peor). Por eso se ESCUCHA de forma continua hasta 30 s y se queda con la mejor, en vez de pedir una
//    sola lectura corta (cada pedido corto reinicia la búsqueda y casi nunca llega a mejorar).
//  - Se "precalienta" al abrir la pantalla de fichar: cuando la persona toca el botón, el GPS ya está enganchado.
//  - Hay una sola búsqueda a la vez; las demás esperan esa misma (dos pedidos simultáneos se pisan en Android).
public class GeolocationService : IGeolocationService
{
    // Con ±20 m ya no se espera más. Si cumple lo que pide el servidor se espera apenas unos segundos más por si mejora.
    private const double PrecisionDeSobraMetros = 20;
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PlazoMaximo = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VigenciaDeUnaLectura = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan EdadMaximaDeLaUltimaConocida = TimeSpan.FromMinutes(2);

    private readonly object _candado = new();
    private Task<Location?>? _busqueda;

    // Mensajes de avance para mostrar en pantalla ("Buscando señal GPS… ±45 m")
    public Action<string>? Progreso { get; set; }

    // Se pide el permiso antes y por separado, para no superponer su diálogo con otros del sistema.
    public async Task<bool> PedirPermisoAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    // Al abrir la pantalla de fichar: empieza a buscar señal sin molestar. Solo si el permiso ya está dado (no abre diálogos)
    // y solo mientras la pantalla está abierta: la app no rastrea en segundo plano.
    public async Task PrecalentarAsync(double precisionRequeridaMetros)
    {
        try
        {
            if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted) return;
            _ = Busqueda(precisionRequeridaMetros);
        }
        catch { /* es un adelanto: si falla, se busca al fichar */ }
    }

    public async Task<UbicacionResultado> ObtenerUbicacionAsync(double precisionRequeridaMetros)
    {
        try
        {
            if (!await PedirPermisoAsync())
                return Fallo("Permiso de ubicación denegado");

            var location = await Busqueda(precisionRequeridaMetros) ?? await UltimaConocidaAsync();
            if (location is null)
                return Fallo("No se pudo obtener la ubicación. Activá el GPS del teléfono (modo de alta precisión) y probá al aire libre.");

            var direccion = await ObtenerDireccionAsync(location.Latitude, location.Longitude);

            return new UbicacionResultado(true, location.Latitude, location.Longitude,
                location.Accuracy, location.IsFromMockProvider, direccion, "OK");
        }
        catch (InvalidOperationException ex) // mensajes pensados para la persona (GPS apagado, sin permiso)
        {
            return Fallo(ex.Message);
        }
        catch (Exception ex)
        {
            return Fallo($"Error de ubicación: {ex.Message}");
        }
    }

    private Task<Location?> Busqueda(double requerida)
    {
        lock (_candado)
        {
            // Ya hay una en marcha (por ejemplo la que se precalentó al abrir la pantalla): se espera esa misma
            if (_busqueda is { IsCompleted: false }) return _busqueda;

            // Una lectura buena de hace poco se reutiliza: volver a fichar enseguida no tiene por qué esperar otra vez
            if (_busqueda is { IsCompletedSuccessfully: true } hecha && hecha.Result is { } ultima
                && DateTimeOffset.UtcNow - ultima.Timestamp < VigenciaDeUnaLectura
                && (ultima.Accuracy ?? double.MaxValue) <= requerida)
                return _busqueda;

            return _busqueda = BuscarAsync(requerida);
        }
    }

    private async Task<Location?> BuscarAsync(double requerida)
    {
        Location? mejor = null;
        var inicio = DateTime.UtcNow;
        var suficiente = new TaskCompletionSource();

        void AlLlegarUnaLectura(object? origen, GeolocationLocationChangedEventArgs e)
        {
            var lectura = e.Location;
            if (lectura is null) return;

            if (mejor is null || (lectura.Accuracy ?? double.MaxValue) < (mejor.Accuracy ?? double.MaxValue))
                mejor = lectura;

            var precision = mejor.Accuracy ?? double.MaxValue;
            if (precision < double.MaxValue) Avisar($"Buscando señal GPS… ±{precision:0} m");

            if (precision <= PrecisionDeSobraMetros || (precision <= requerida && DateTime.UtcNow - inicio >= EsperaMinima))
                suficiente.TrySetResult();
        }

        bool escuchando = false;
        Geolocation.Default.LocationChanged += AlLlegarUnaLectura;
        try
        {
            Avisar("Buscando señal GPS…");
            escuchando = await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));

            if (escuchando)
                await Task.WhenAny(suficiente.Task, Task.Delay(PlazoMaximo));
        }
        catch (FeatureNotEnabledException)
        {
            throw new InvalidOperationException("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar.");
        }
        catch (PermissionException)
        {
            throw new InvalidOperationException("Falta el permiso de ubicación. Habilitalo en Ajustes → Apps → Fichaje → Permisos.");
        }
        catch
        {
            escuchando = false; // el teléfono no soporta la escucha continua: se usa el pedido único de abajo
        }
        finally
        {
            Geolocation.Default.LocationChanged -= AlLlegarUnaLectura;
            if (escuchando)
            {
                try { Geolocation.Default.StopListeningForeground(); } catch { /* ya estaba detenida */ }
            }
        }

        return mejor ?? await LecturaUnicaAsync();
    }

    // Respaldo: el pedido único de siempre (más lento y menos preciso, pero funciona en cualquier teléfono)
    private static async Task<Location?> LecturaUnicaAsync()
    {
        try
        {
            using var cancelar = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            return await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)), cancelar.Token);
        }
        catch (FeatureNotEnabledException) { throw new InvalidOperationException("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar."); }
        catch (PermissionException) { throw new InvalidOperationException("Falta el permiso de ubicación. Habilitalo en Ajustes → Apps → Fichaje → Permisos."); }
        catch { return null; }
    }

    // Última posición que el teléfono ya tenía, si es reciente: mejor eso que dejar a la persona sin poder fichar
    // (el servidor igual verifica la precisión y la guarda en el fichaje)
    private static async Task<Location?> UltimaConocidaAsync()
    {
        try
        {
            var ultima = await Geolocation.Default.GetLastKnownLocationAsync();
            return ultima is not null && DateTimeOffset.UtcNow - ultima.Timestamp <= EdadMaximaDeLaUltimaConocida ? ultima : null;
        }
        catch { return null; }
    }

    private void Avisar(string texto) => Progreso?.Invoke(texto);

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
