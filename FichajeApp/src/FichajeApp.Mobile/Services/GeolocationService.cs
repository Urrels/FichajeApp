namespace FichajeApp.Mobile.Services;

public record UbicacionResultado(
    bool Exito, double Lat, double Lon, double? Precision, bool EsFalsa, string? Direccion, string Mensaje);

// Ubicación para fichar. La idea: que al tocar el botón la posición YA esté.
//  - Al abrir la pantalla de fichar se pide el permiso (una sola vez, ahí y no al fichar) y se empieza a escuchar a todos los
//    proveedores del teléfono a la vez: GPS, WiFi y antenas (ver BuscadorDeUbicacion y FuenteDeUbicacionAndroid). La red da una
//    posición razonable en 1 a 3 s aun adentro de un edificio; el GPS mejora con el tiempo.
//  - Se escucha mientras la pantalla está abierta y se apaga al salir, al pasar la app a segundo plano o a los 10 minutos:
//    la app NO rastrea en segundo plano (ni consume batería ni sigue a nadie fuera de su pantalla).
//  - Al fichar se usa la mejor lectura reciente que cumpla la precisión que acepta el servidor, sin esperar.
//  - La dirección (calle) se busca de antemano: antes sumaba hasta 1,5 s al momento de fichar.
public class GeolocationService : IGeolocationService
{
    private static readonly TimeSpan TiempoMaximoEscuchando = TimeSpan.FromMinutes(10);
    private const double MetrosParaReusarDireccion = 100;

    private readonly BuscadorDeUbicacion _buscador;
    private readonly object _candado = new();
    private CancellationTokenSource? _apagado;
    private (double Lat, double Lon, string? Texto)? _direccion;

    public GeolocationService(IFuenteDeUbicacion fuente) => _buscador = new BuscadorDeUbicacion(fuente);

    // Mensajes de avance para mostrar en pantalla ("Buscando señal GPS… ±45 m")
    public Action<string>? Progreso
    {
        get => _buscador.Progreso;
        set => _buscador.Progreso = value;
    }

    // Se pide el permiso antes y por separado, para no superponer su diálogo con otros del sistema.
    public async Task<bool> PedirPermisoAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    // Al abrir la pantalla de fichar: permiso (si falta) y a escuchar. No molesta si falla: se vuelve a intentar al fichar.
    public async Task PrecalentarAsync(double precisionRequeridaMetros)
    {
        try
        {
            if (!await PedirPermisoAsync()) return;
            _buscador.Iniciar();
            ProgramarApagado();
            _ = PrepararDireccionAsync(precisionRequeridaMetros);
        }
        catch { /* es un adelanto: si falla, se busca al fichar */ }
    }

    // Al salir de la pantalla o pasar la app a segundo plano
    public void Detener()
    {
        lock (_candado) { _apagado?.Cancel(); _apagado = null; }
        try { _buscador.Detener(); } catch { /* ya estaba detenida */ }
    }

    public async Task<UbicacionResultado> ObtenerUbicacionAsync(double precisionRequeridaMetros)
    {
        try
        {
            if (!await PedirPermisoAsync())
                return Fallo("Permiso de ubicación denegado");

            var lectura = await _buscador.EsperarAsync(precisionRequeridaMetros) ?? await LecturaDeRespaldoAsync();
            if (lectura is null)
                return Fallo("No se pudo obtener la ubicación. Activá el GPS del teléfono (modo de alta precisión) y probá al aire libre.");

            var direccion = await DireccionAsync(lectura.Lat, lectura.Lon);
            return new UbicacionResultado(true, lectura.Lat, lectura.Lon, lectura.Precision, lectura.EsFalsa, direccion, "OK");
        }
        catch (InvalidOperationException ex) // mensajes pensados para la persona (GPS apagado, sin permiso)
        {
            return Fallo(ex.Message);
        }
        catch (Exception)
        {
            return Fallo(TextosDeError.UbicacionNoDisponible);
        }
    }

    private void ProgramarApagado()
    {
        CancellationTokenSource nuevo;
        lock (_candado)
        {
            _apagado?.Cancel();
            _apagado = nuevo = new CancellationTokenSource();
        }
        _ = Task.Delay(TiempoMaximoEscuchando, nuevo.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) try { _buscador.Detener(); } catch { /* ya estaba detenida */ }
        }, TaskScheduler.Default);
    }

    // Respaldo para teléfonos donde la escucha no entregó nada: el pedido único de MAUI
    private static async Task<LecturaGps?> LecturaDeRespaldoAsync()
    {
        try
        {
            using var cancelar = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var l = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(8)), cancelar.Token);
            return l is null ? null : new LecturaGps(l.Latitude, l.Longitude, l.Accuracy, l.Timestamp, l.IsFromMockProvider, "maui");
        }
        catch (FeatureNotEnabledException) { throw new InvalidOperationException("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar."); }
        catch (PermissionException) { throw new InvalidOperationException("Falta el permiso de ubicación. Habilitalo en Ajustes → Apps → Fichaje → Permisos."); }
        catch { return null; }
    }

    // ---- Dirección ----

    // En cuanto hay una primera lectura se busca la calle y se guarda: al fichar ya está
    private async Task PrepararDireccionAsync(double precisionRequerida)
    {
        try
        {
            var lectura = await _buscador.EsperarAsync(precisionRequerida, TimeSpan.FromSeconds(20));
            if (lectura is not null) await DireccionAsync(lectura.Lat, lectura.Lon);
        }
        catch { /* es un adelanto */ }
    }

    // Si ya se buscó la dirección de un lugar a menos de 100 m, se reutiliza. La dirección es opcional: si falla o tarda
    // más de 1,5 s, el fichaje sigue sin ella.
    private async Task<string?> DireccionAsync(double lat, double lon)
    {
        lock (_candado)
        {
            if (_direccion is { } d && DistanciaEnMetros(lat, lon, d.Lat, d.Lon) <= MetrosParaReusarDireccion)
                return d.Texto;
        }

        var texto = await ObtenerDireccionAsync(lat, lon);
        if (texto is not null) lock (_candado) _direccion = (lat, lon, texto);
        return texto;
    }

    private static async Task<string?> ObtenerDireccionAsync(double lat, double lon)
    {
        try
        {
            var geocoding = Geocoding.Default.GetPlacemarksAsync(lat, lon);
            if (await Task.WhenAny(geocoding, Task.Delay(TimeSpan.FromMilliseconds(1500))) != geocoding)
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

    private static double DistanciaEnMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static UbicacionResultado Fallo(string mensaje) =>
        new(false, 0, 0, null, false, null, mensaje);
}
