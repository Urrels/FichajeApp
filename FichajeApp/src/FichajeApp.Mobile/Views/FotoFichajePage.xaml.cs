using System.Globalization;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Muestra la foto de un fichaje (solo supervisores): se baja del servidor con la sesión del usuario
[QueryProperty(nameof(Archivo), "archivo")]
[QueryProperty(nameof(Titulo), "titulo")]
[QueryProperty(nameof(Lat), "lat")]
[QueryProperty(nameof(Lon), "lon")]
public partial class FotoFichajePage : ContentPage
{
    private readonly ApiService _api;

    public string Archivo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Lat { get; set; } = string.Empty;
    public string Lon { get; set; } = string.Empty;

    public FotoFichajePage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    private bool TryUbicacion(out double lat, out double lon)
    {
        lat = lon = 0;
        return double.TryParse(Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && (lat != 0 || lon != 0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Detalle.Text = Titulo;
        MapsButton.IsVisible = TryUbicacion(out _, out _);
        Cargando.IsRunning = Cargando.IsVisible = true;

        var bytes = string.IsNullOrEmpty(Archivo) ? null : await _api.DescargarFotoAsync(Archivo);
        Cargando.IsRunning = Cargando.IsVisible = false;

        if (bytes is null) Detalle.Text = $"{Titulo}\nNo se pudo cargar la foto. Puede que ya se haya borrado (las fotos se guardan un tiempo limitado).";
        else Foto.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private async void OnVerEnMapsClicked(object? sender, EventArgs e)
    {
        if (TryUbicacion(out var lat, out var lon)) await EnlaceMapas.AbrirAsync(lat, lon);
    }
}
