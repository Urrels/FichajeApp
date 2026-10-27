using Microsoft.Extensions.Logging;
using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;
using FichajeApp.Mobile.Views;

namespace FichajeApp.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // NOTA: SessionManager NO se registra acá — es un Singleton clásico (GoF),
        // se accede vía SessionManager.Instance, no necesita el contenedor de DI.
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<IGeolocationService, GeolocationService>();
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddTransient<FichajeViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<FichajePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
