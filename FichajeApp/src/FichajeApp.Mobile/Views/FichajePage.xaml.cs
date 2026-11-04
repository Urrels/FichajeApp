using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    private readonly AuthService _authService;
    private bool _visible;
    private static readonly System.Globalization.CultureInfo Es = new("es-AR");

    public FichajePage(FichajeViewModel viewModel, AuthService authService)
    {
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
        await _viewModel.ActualizarProximoAsync();
        var sesion = SessionManager.Instance;
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
        base.OnDisappearing();
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
