using System.Globalization;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class SupervisorPage : ContentPage
{
    private readonly SupervisorViewModel _viewModel;

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
        await _viewModel.IniciarAsync();
    }

    private async void OnHoyClicked(object sender, EventArgs e) => await _viewModel.MostrarHoyAsync();
    private async void OnPresentesClicked(object sender, EventArgs e) => await _viewModel.MostrarPresentesAsync();
    private async void OnHistorialClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();
    private async void OnBuscarClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();

    // Al elegir un fichaje de la lista, el mapa se centra en ese punto
    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is FichajeItem f)
            Mapa.MoveToRegion(MapSpan.FromCenterAndRadius(
                new Location(f.Latitud, f.Longitud), Distance.FromMeters(250)));
    }

    private void RedibujarMapa()
    {
        Mapa.Pins.Clear();
        Mapa.MapElements.Clear();

        var items = _viewModel.Items;
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
