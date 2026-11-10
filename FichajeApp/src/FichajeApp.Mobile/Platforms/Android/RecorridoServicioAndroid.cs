using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

// Servicio en primer plano que guarda la ubicación cada pocos minutos MIENTRAS HAY UNA JORNADA ABIERTA.
// Se inicia desde la app (cuando está a la vista, justo después de fichar la entrada) y se detiene al fichar la salida,
// al cerrar sesión o cuando el servidor dice que pare. Android obliga a mostrar una notificación fija mientras corre.
// No usa el permiso de ubicación en segundo plano: un servicio en primer plano iniciado con la app a la vista conserva el acceso.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public class RecorridoServicioAndroid : Service, ILocationListener
{
    private const string Canal = "recorrido";
    private const int IdNotificacion = 7421;
    private const long IntervaloMs = 3 * 60 * 1000;     // una lectura cada 3 minutos
    private static readonly TimeSpan SeparacionMinima = TimeSpan.FromSeconds(90);

    public static bool Activo { get; private set; }

    private DateTime _ultimoPunto = DateTime.MinValue;

    public static void Iniciar()
    {
        var contexto = Android.App.Application.Context;
        var intent = new Intent(contexto, typeof(RecorridoServicioAndroid));
        contexto.StartForegroundService(intent);
    }

    public static void Detener()
    {
        var contexto = Android.App.Application.Context;
        contexto.StopService(new Intent(contexto, typeof(RecorridoServicioAndroid)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Si el sistema reinició el servicio sin que haya sesión, no hay nada que registrar
        if (!RecorridoEnvio.HaySesion())
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        StartForeground(IdNotificacion, CrearNotificacion(), ForegroundService.TypeLocation);
        Activo = true;

        try { EmpezarAEscuchar(); }
        catch (Exception)
        {
            // Sin permiso de ubicación o GPS apagado: no hay nada que registrar
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        return StartCommandResult.Sticky;
    }

    private void EmpezarAEscuchar()
    {
        var manager = (LocationManager?)GetSystemService(LocationService);
        if (manager is null) return;

        foreach (var proveedor in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
        {
            if (manager.IsProviderEnabled(proveedor))
                manager.RequestLocationUpdates(proveedor, IntervaloMs, 0f, this, Looper.MainLooper);
        }
    }

    private Notification CrearNotificacion()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(Canal, "Recorrido durante la jornada", NotificationImportance.Low)
        {
            Description = "Se muestra mientras la app registra tu ubicación durante tu jornada."
        });

        var abrir = PendingIntent.GetActivity(this, 0,
            new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new Notification.Builder(this, Canal)
            .SetContentTitle("Registrando tu recorrido")
            .SetContentText("Solo mientras tu jornada está abierta. Se detiene al fichar la salida.")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .SetContentIntent(abrir)
            .Build()!;
    }

    public void OnLocationChanged(Android.Locations.Location location)
    {
        // El GPS y la red pueden contestar casi a la vez: se queda con una lectura cada tanto
        if (DateTime.UtcNow - _ultimoPunto < SeparacionMinima) return;

        // Una ubicación simulada no es un recorrido real
        #pragma warning disable CA1422
        var simulada = location.IsFromMockProvider;
#pragma warning restore CA1422
        if (simulada) return;

        _ultimoPunto = DateTime.UtcNow;
        RecorridoEnvio.Agregar(new PuntoPendiente
        {
            FechaHora = DateTimeOffset.FromUnixTimeMilliseconds(location.Time),
            Latitud = location.Latitude,
            Longitud = location.Longitude,
            Precision = location.HasAccuracy ? location.Accuracy : null
        });
        _ = RecorridoEnvio.EnviarPendientesAsync();
    }

    public void OnProviderDisabled(string provider) { }
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    public override void OnDestroy()
    {
        Activo = false;
        try { ((LocationManager?)GetSystemService(LocationService))?.RemoveUpdates(this); } catch { }
        base.OnDestroy();
    }
}
