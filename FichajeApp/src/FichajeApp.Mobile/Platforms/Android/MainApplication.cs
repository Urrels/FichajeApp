using Android.App;
using Android.OS;
using Android.Runtime;
using Plugin.Fingerprint;

namespace FichajeApp.Mobile;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        RegisterActivityLifecycleCallbacks(new FingerprintActivityLifecycleCallbacks());
    }
}

// Le indica a Plugin.Fingerprint cuál es la Activity actual en todo momento,
// necesario para poder disparar el diálogo nativo de biometría.
public class FingerprintActivityLifecycleCallbacks : Java.Lang.Object, Android.App.Application.IActivityLifecycleCallbacks
{
    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }

    public void OnActivityResumed(Activity activity)
    {
        CrossFingerprint.SetCurrentActivityResolver(() => activity);
    }

    public void OnActivityPaused(Activity activity) { }
    public void OnActivityDestroyed(Activity activity) { }
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}