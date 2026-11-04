using System.Globalization;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class SupervisorPage : ContentPage
{
    private static readonly TimeSpan IntervaloActualizacion = TimeSpan.FromSeconds(30);

    private readonly SupervisorViewModel _viewModel;
    private bool _visible;

    public SupervisorPage(ApiService apiService)
    {
        InitializeComponent();
        _viewModel = new SupervisorViewModel(apiService);
        _viewModel.DatosActualizados += () => MainThread.BeginInvokeOnMainThread(RedibujarMapa);
        _viewModel.SesionFinalizada += OnSesionFinalizada;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AvisoMapa.IsVisible = FaltaClaveDeMaps();
        _visible = true;
        // Mientras la pantalla está a la vista, "Hoy" y "Presentes" se ponen al día solos: antes había que volver a tocar el botón
        Dispatcher.StartTimer(IntervaloActualizacion, () =>
        {
            if (_visible && !_viewModel.EsHistorial) _ = _viewModel.RefrescarAsync();
            return _visible;
        });
        await _viewModel.IniciarAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        base.OnDisappearing();
    }

    // La clave de Maps vive en el manifest de Android; si quedó el valor de ejemplo el mapa sale en blanco
    private static bool FaltaClaveDeMaps()
    {
#if ANDROID
        try
        {
            var contexto = Android.App.Application.Context;
#pragma warning disable CA1422
            var info = contexto.PackageManager!.GetApplicationInfo(contexto.PackageName!, Android.Content.PM.PackageInfoFlags.MetaData);
#pragma warning restore CA1422
            var clave = info.MetaData?.GetString("com.google.android.geo.API_KEY");
            return string.IsNullOrWhiteSpace(clave) || clave.StartsWith("TU_API_KEY", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
#else
        return false;
#endif
    }

    private async void OnHoyClicked(object sender, EventArgs e) => await _viewModel.MostrarHoyAsync();
    private async void OnPresentesClicked(object sender, EventArgs e) => await _viewModel.MostrarPresentesAsync();
    private async void OnHistorialClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();
    private async void OnBuscarClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();

    // Al elegir un fichaje de la lista, el mapa se centra en ese punto; si tiene foto, se abre
    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FichajeItem f) return;

        if (f.TieneUbicacion)
            Mapa.MoveToRegion(MapSpan.FromCenterAndRadius(
                new Location(f.Latitud, f.Longitud), Distance.FromMeters(250)));

        if (f.TieneFoto)
        {
            ((CollectionView)sender).SelectedItem = null;
            await Shell.Current.GoToAsync($"{nameof(FotoFichajePage)}?archivo={Uri.EscapeDataString(f.Foto!)}" +
                $"&titulo={Uri.EscapeDataString($"{f.NombreCompleto} · {f.TipoFichaje} {f.FechaHora:dd/MM HH:mm}")}");
        }
    }

    private void RedibujarMapa()
    {
        Mapa.Pins.Clear();
        Mapa.MapElements.Clear();

        // Los fichajes sin ubicación (cliente que no la pide) no se dibujan en el mapa
        var items = _viewModel.Items.Where(i => i.TieneUbicacion).ToList();
        if (items.Count == 0) return;

        foreach (var f in items)
        {
            var enlace = string.Create(CultureInfo.InvariantCulture,
                $"https://www.google.com/maps?q={f.Latitud},{f.Longitud}");

            var pin = new Pin
            {
                Label = $"{f.NombreCompleto} · {f.FechaHora:HH:mm}",
                Address = $"{f.TipoFichaje} · {f.Lugar}",
                Location = new Location(f.Latitud, f.Longitud),
                Type = PinType.Place
            };
            // Tocar el globo del pin abre ese punto en Google Maps
            pin.InfoWindowClicked += async (_, _) => await Launcher.Default.OpenAsync(new Uri(enlace));
            Mapa.Pins.Add(pin);
        }

        if (_viewModel.HayRecorrido)
        {
            var linea = new Polyline { StrokeColor = Colors.Blue, StrokeWidth = 6 };
            foreach (var f in items)
                linea.Geopath.Add(new Location(f.Latitud, f.Longitud));
            Mapa.MapElements.Add(linea);
        }

        // En una actualización automática no se mueve el mapa: alguien puede estar mirando otra zona
        if (!_viewModel.EncuadrarMapa) return;

        // Encuadra todos los puntos (con un mínimo de zoom para un solo punto)
        var minLat = items.Min(f => f.Latitud);
        var maxLat = items.Max(f => f.Latitud);
        var minLon = items.Min(f => f.Longitud);
        var maxLon = items.Max(f => f.Longitud);
        var centro = new Location((minLat + maxLat) / 2, (minLon + maxLon) / 2);
        Mapa.MoveToRegion(new MapSpan(centro,
            Math.Max((maxLat - minLat) * 1.4, 0.005),
            Math.Max((maxLon - minLon) * 1.4, 0.005)));
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
