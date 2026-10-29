using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Muestra la foto de un fichaje (solo supervisores): se baja del servidor con la sesión del usuario
[QueryProperty(nameof(Archivo), "archivo")]
[QueryProperty(nameof(Titulo), "titulo")]
public partial class FotoFichajePage : ContentPage
{
    private readonly ApiService _api;

    public string Archivo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;

    public FotoFichajePage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Detalle.Text = Titulo;
        Cargando.IsRunning = Cargando.IsVisible = true;

        var bytes = string.IsNullOrEmpty(Archivo) ? null : await _api.DescargarFotoAsync(Archivo);
        Cargando.IsRunning = Cargando.IsVisible = false;

        if (bytes is null) Detalle.Text = $"{Titulo}\nNo se pudo cargar la foto.";
        else Foto.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
    }
}
