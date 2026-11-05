using Android.Content;
using Android.Locations;
using Android.OS;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

// Escucha a la vez a todos los proveedores de ubicación del teléfono: el GPS (preciso, pero tarda en "enganchar" satélites),
// la red (WiFi y antenas: una posición razonable en 1 a 3 segundos, incluso adentro de un edificio) y, en Android 12 o más,
// el proveedor combinado del sistema. BuscadorDeUbicacion se queda con la mejor lectura reciente de cualquiera de ellos.
// Solo escucha mientras la pantalla de fichar está abierta: la app no rastrea en segundo plano.
public sealed class FuenteDeUbicacionAndroid : Java.Lang.Object, ILocationListener, IFuenteDeUbicacion
{
    private static readonly string[] Preferidos = { LocationManager.GpsProvider, LocationManager.NetworkProvider };

    public event Action<LecturaGps>? Lectura;
    public bool Activa { get; private set; }

    private static LocationManager? Manager =>
        Android.App.Application.Context.GetSystemService(Context.LocationService) as LocationManager;

    public void Iniciar()
    {
        var manager = Manager ?? throw new InvalidOperationException("Este teléfono no tiene servicio de ubicación.");

        var proveedores = ProveedoresActivos(manager);
        if (proveedores.Count == 0)
            throw new InvalidOperationException("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar.");

        var escuchando = 0;
        foreach (var proveedor in proveedores)
        {
            try
            {
                // Cada 1 s, sin distancia mínima, en el hilo principal
                manager.RequestLocationUpdates(proveedor, 1000, 0f, this, Looper.MainLooper!);
                escuchando++;
            }
            catch (Java.Lang.SecurityException)
            {
                throw new InvalidOperationException("Falta el permiso de ubicación. Habilitalo en Ajustes → Apps → Fichaje → Permisos.");
            }
            catch (Exception)
            {
                // Un proveedor que falla no impide usar los otros
            }
        }
        Activa = escuchando > 0;
    }

    public void Detener()
    {
        Activa = false;
        try { Manager?.RemoveUpdates(this); }
        catch { /* ya estaba detenida */ }
    }

    public IReadOnlyList<LecturaGps> UltimasConocidas()
    {
        var resultado = new List<LecturaGps>();
        try
        {
            var manager = Manager;
            if (manager is null) return resultado;

            foreach (var proveedor in ProveedoresActivos(manager))
            {
                var ultima = manager.GetLastKnownLocation(proveedor);
                if (ultima is not null) resultado.Add(Convertir(ultima));
            }
        }
        catch { /* sin permiso o sin servicio: se arranca sin nada */ }
        return resultado;
    }

    public void OnLocationChanged(Android.Locations.Location location) => Lectura?.Invoke(Convertir(location));

    public void OnProviderDisabled(string provider) { }
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    // Los que están prendidos ahora. "passive" no se pide (solo repite lo que piden otras apps).
    private static List<string> ProveedoresActivos(LocationManager manager)
    {
        var activos = manager.GetProviders(true) ?? new List<string>();
        var lista = Preferidos.Where(activos.Contains).ToList();
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && activos.Contains(LocationManager.FusedProvider))
            lista.Add(LocationManager.FusedProvider);
        return lista;
    }

    private static LecturaGps Convertir(Android.Locations.Location l) => new(
        l.Latitude,
        l.Longitude,
        l.HasAccuracy ? l.Accuracy : null,
        DateTimeOffset.FromUnixTimeMilliseconds(l.Time),
#pragma warning disable CA1422 // IsFromMockProvider está en desuso desde Android 12: acá se usa IsMock cuando existe
        OperatingSystem.IsAndroidVersionAtLeast(31) ? l.Mock : l.IsFromMockProvider,
#pragma warning restore CA1422
        l.Provider ?? "?");
}
