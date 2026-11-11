using Android.App;
using Android.Content.PM;
using Android.OS;

namespace FichajeApp.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Ninguna pantalla de la app participa del autocompletado del sistema (ver MauiProgram)
        if (Window?.DecorView is { } raiz) raiz.ImportantForAutofill = Android.Views.ImportantForAutofill.NoExcludeDescendants;
    }
}
