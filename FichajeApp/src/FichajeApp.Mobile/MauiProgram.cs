using CommunityToolkit.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;
using UraniumUI;
using UraniumUI.Icons.MaterialSymbols;
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
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitCamera()
            .UseUraniumUI()
            .UseUraniumUIMaterial()
            .ConfigureFonts(fonts =>
            {
                fonts.AddMaterialSymbolsFonts();
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID
        // Las fechas y listas nativas de Android traen una línea inferior: dentro del campo con borde (estilo "Campo") sobra
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("SinLinea", (handler, _) =>
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("SinLinea", (handler, _) =>
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#endif

        // NOTA: SessionManager NO se registra acá — es un Singleton clásico (GoF),
        // se accede vía SessionManager.Instance, no necesita el contenedor de DI.
        builder.Services.AddSingleton<IClienteService, ClienteService>();
        builder.Services.AddSingleton<AparienciaService>();
        builder.Services.AddSingleton<IFeedback, Feedback>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ConfiguracionApiService>();
        builder.Services.AddTransient<ConfiguracionViewModel>();
        builder.Services.AddTransient<ConfiguracionPage>();
        builder.Services.AddTransient<AyudaClavePage>();
        builder.Services.AddTransient<ConfigFichajeViewModel>();
        builder.Services.AddTransient<ConfigFichajePage>();
        builder.Services.AddTransient<FotoFichajePage>();
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<IFuenteDeUbicacion, FuenteDeUbicacionAndroid>();
        builder.Services.AddSingleton<IGeolocationService, GeolocationService>();
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddTransient<FichajeViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<FichajePage>();
        builder.Services.AddTransient<SupervisorPage>();
        builder.Services.AddTransient<KioscoViewModel>();
        builder.Services.AddTransient<KioscoPage>();

        // Administración: usuarios, roles, bitácora, corrección de fichajes y liquidación
        builder.Services.AddSingleton<AdminApiService>();
        builder.Services.AddSingleton<PerfilFotos>();
        builder.Services.AddTransient<UsuariosViewModel>();
        builder.Services.AddTransient<SitiosViewModel>();
        builder.Services.AddTransient<RolesViewModel>();
        builder.Services.AddTransient<BitacoraViewModel>();
        builder.Services.AddTransient<FichajesAdminViewModel>();
        builder.Services.AddTransient<LiquidacionViewModel>();
        builder.Services.AddTransient<AparienciaViewModel>();
        builder.Services.AddTransient<AdminPage>();
        builder.Services.AddTransient<UsuariosPage>();
        builder.Services.AddTransient<UsuarioNuevoPage>();
        builder.Services.AddTransient<SitiosPage>();
        builder.Services.AddTransient<RolesPage>();
        builder.Services.AddTransient<BitacoraPage>();
        builder.Services.AddTransient<FichajesAdminPage>();
        builder.Services.AddTransient<LiquidacionPage>();
        builder.Services.AddTransient<CambiarPinPage>();
        builder.Services.AddTransient<AparienciaPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
