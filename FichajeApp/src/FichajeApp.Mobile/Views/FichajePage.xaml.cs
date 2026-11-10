using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    private readonly AuthService _authService;
    private bool _visible;
    private static readonly System.Globalization.CultureInfo Es = new("es-AR");

    private readonly PerfilFotos _perfiles;
    private readonly IFeedback _feedback;

    public FichajePage(FichajeViewModel viewModel, AuthService authService, PerfilFotos perfiles, IFeedback feedback)
    {
        _perfiles = perfiles;
        _feedback = feedback;
        InitializeComponent();
        _authService = authService;
        _viewModel = viewModel;
        _viewModel.SesionFinalizada += OnSesionFinalizada;
        BindingContext = _viewModel;
        SizeChanged += (_, _) => AjustarBoton();
        Cabecera.SizeChanged += (_, _) => AjustarBoton();
        Pie.SizeChanged += (_, _) => AjustarBoton();   // el pie cambia de alto cuando aparece un mensaje
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        ActualizarReloj();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () => { ActualizarReloj(); return _visible; });
        _ = RespirarAsync();
        _ = _viewModel.PrepararUbicacionAsync(); // sin esperar: el GPS engancha mientras la persona se prepara (y antes de la consulta al servidor)
        if (Window is { } ventana)
        {
            // La ubicación solo se escucha con la app a la vista: al pasar a segundo plano se apaga y al volver se retoma
            ventana.Stopped += AlPasarASegundoPlano;
            ventana.Resumed += AlVolverALaApp;
        }
        await _viewModel.ActualizarProximoAsync();
        var sesion = SessionManager.Instance;
        MiFoto.Nombre = sesion.NombreCompleto ?? string.Empty;
        MiFoto.EmpleadoId = sesion.EmpleadoId ?? 0;
        PinButton.IsVisible = sesion.TienePermiso(NombresPermisos.CambiarPin);
        PanelButton.IsVisible = sesion.EsSupervisor || sesion.EsAdministrador;
    }

    // En una pantalla corta (o con la letra grande del sistema) el botón de 200 quedaba tapado por lo de abajo: se achica a lo que entra
    private void AjustarBoton()
    {
        if (Height <= 0 || Cabecera.Height <= 0 || Pie.Height <= 0) return;
        var libre = Height - Cabecera.Height - Pie.Height;
        var lado = Math.Clamp(libre - 70, 120, 200);
        BotonFichar.WidthRequest = BotonFichar.HeightRequest = lado;
        Brillo.WidthRequest = Brillo.HeightRequest = lado;
        Halo.WidthRequest = Halo.HeightRequest = lado + 50;
        TextoBoton.MaximumWidthRequest = lado * 0.78;   // "Fichar entrada" pasa a dos renglones en vez de salirse del círculo
        AvisoPermisos.IsVisible = libre > 380;
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        if (Window is { } ventana)
        {
            ventana.Stopped -= AlPasarASegundoPlano;
            ventana.Resumed -= AlVolverALaApp;
        }
        _viewModel.DetenerUbicacion();   // fuera de esta pantalla la app no escucha el GPS
        base.OnDisappearing();
    }

    private void AlPasarASegundoPlano(object? sender, EventArgs e) => _viewModel.DetenerUbicacion();

    private void AlVolverALaApp(object? sender, EventArgs e)
    {
        if (_visible) _ = _viewModel.PrepararUbicacionAsync();
    }

    private void ActualizarReloj()
    {
        var ahora = DateTime.Now;
        HoraLabel.Text = ahora.ToString("HH:mm", Es);
        var fecha = ahora.ToString("dddd d 'de' MMMM", Es);
        FechaLabel.Text = char.ToUpper(fecha[0], Es) + fecha[1..];
    }

    // El halo se agranda y achica despacio: invita a tocar sin distraer
    private async Task RespirarAsync()
    {
        while (_visible)
        {
            // Con las animaciones del sistema apagadas ScaleTo termina al instante: el Delay evita un bucle sin pausa
            await Task.WhenAll(Halo.ScaleTo(1.08, 1400, Easing.SinInOut), Task.Delay(1400));
            await Task.WhenAll(Halo.ScaleTo(1.0, 1400, Easing.SinInOut), Task.Delay(1400));
        }
    }

    // Tocar la propia foto: sacar una nueva con la cámara frontal o quitarla (vuelven a verse las iniciales)
    private async void OnMiFotoTapped(object? sender, TappedEventArgs e)
    {
        // La foto de perfil es obligatoria: se puede cambiar, pero no quitar
        const string Nueva = "Sacar una foto nueva";
        var elegida = await Dialogos.Elegir("Tu foto de perfil", new[] { Nueva });
        if (elegida is null) return;

        try
        {
            if (elegida == Nueva)
            {
                var foto = await FotoDePerfil.TomarValidadaAsync();
                if (foto is null) return;   // canceló
                var r = await _perfiles.GuardarAsync(null, foto);
                await _feedback.ResultadoAsync(r.Exito, "Foto de perfil actualizada.", r.Mensaje);
            }
            else
            {
                var r = await _perfiles.QuitarAsync(null);
                await _feedback.ResultadoAsync(r.Exito, "Foto de perfil quitada.", r.Mensaje);
            }
        }
        catch (Exception ex)
        {
            await Dialogos.Aviso("No se pudo", TextosDeError.DeExcepcion(ex));
        }
    }

    private async void OnVolverAlPanel(object sender, EventArgs e) => await Shell.Current.GoToAsync(Navegacion.RutaInicial());

    private async void OnCambiarPin(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(CambiarPinPage));

    private void OnCerrarSesion(object sender, EventArgs e) => _authService.Logout();

    private async void OnFicharTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.Procesando) return; // mientras trabaja, el toque no hace nada (antes el botón se deshabilitaba)
        await _viewModel.FicharAsync();
    }

    private async void OnAbrirMapsClicked(object sender, EventArgs e)
    {
        await _viewModel.AbrirEnMapsAsync();
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
