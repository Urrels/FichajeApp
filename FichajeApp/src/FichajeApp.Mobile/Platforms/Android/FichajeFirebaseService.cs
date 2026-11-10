using Android.App;
using Android.Content;
using Firebase.Messaging;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile;

// Canal de los avisos de la cuenta (push). Tiene que existir antes de que llegue el primero: Firebase lo usa para mostrarlo.
public static class CanalDeAvisos
{
    public const string Id = "avisos";

    public static void Crear(Context contexto)
    {
        var manager = (NotificationManager?)contexto.GetSystemService(Context.NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(Id, "Avisos de tu cuenta", NotificationImportance.Default)
        {
            Description = "Cambios de PIN, de horario y otros avisos de tu empresa."
        });
    }
}

// Recibe los push de Firebase. Con la app cerrada o en segundo plano, Android muestra el aviso solo;
// con la app abierta llega acá y se muestra a mano.
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class FichajeFirebaseService : FirebaseMessagingService
{
    // Firebase le asignó un token nuevo a este teléfono: se le avisa al servidor (si hay una sesión iniciada)
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        Preferences.Default.Set(PushDispositivo.ClaveToken, token);
        if (SessionManager.Instance.EstaLogueado) _ = PushDispositivo.RegistrarAsync();
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var aviso = message.GetNotification();
        if (aviso is null) return;

        CanalDeAvisos.Crear(this);
        var abrir = PendingIntent.GetActivity(this, 0,
            new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notificacion = new Notification.Builder(this, CanalDeAvisos.Id)
            .SetContentTitle(aviso.Title)
            .SetContentText(aviso.Body)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .SetContentIntent(abrir)
            .Build();

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(Environment.TickCount & 0x7FFFFFFF, notificacion);
    }
}
