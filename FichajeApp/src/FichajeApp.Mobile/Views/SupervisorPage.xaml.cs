using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

// El mapa es OpenStreetMap con Mapsui: gratis y sin clave (Google Maps pedía una clave y facturación activa)
public partial class SupervisorPage : ContentPage
{
    private static readonly TimeSpan IntervaloActualizacion = TimeSpan.FromSeconds(30);

    // Entrada en verde y salida en ámbar (los mismos tonos que las etiquetas del kiosco)
    private static readonly Color ColorEntrada = Color.FromArgb("#2E7D32");
    private static readonly Color ColorSalida = Color.FromArgb("#B26A00");

    private readonly SupervisorViewModel _viewModel;
    private bool _visible;

    public SupervisorPage(ApiService apiService)
    {
        InitializeComponent();
        _viewModel = new SupervisorViewModel(apiService);
        _viewModel.DatosActualizados += () => MainThread.BeginInvokeOnMainThread(RedibujarMapa);
        _viewModel.SesionFinalizada += OnSesionFinalizada;
        BindingContext = _viewModel;

        // No se reemplaza Mapa.Map: MapView ya le agregó sus capas de pines y dibujos, y con un Map nuevo no se ven
        // OpenStreetMap pide identificar la app en el User-Agent
        Mapa.Map.Layers.Insert(0, OpenStreetMap.CreateTileLayer("FichajeApp-supervisor"));
        Mapa.IsMyLocationButtonVisible = false;
        Mapa.IsZoomButtonVisible = true;
        // Tocar un pin muestra quién es, a qué hora y dónde
        Mapa.PinClicked += (_, e) =>
        {
            if (e.Pin is null) return;
            e.Pin.ShowCallout();
            e.Handled = true;
        };
        // Antes de que haya fichajes se muestra Argentina entera en vez del océano
        CentrarEn(-34.6037, -58.3816, resolucion: 600);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
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

    private async void OnHoyClicked(object sender, EventArgs e) => await _viewModel.MostrarHoyAsync();
    private async void OnPresentesClicked(object sender, EventArgs e) => await _viewModel.MostrarPresentesAsync();
    private async void OnHistorialClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();
    private async void OnBuscarClicked(object sender, EventArgs e) => await _viewModel.BuscarHistorialAsync();

    // Al elegir un fichaje de la lista, el mapa se centra en ese punto; si tiene foto, se abre
    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FichajeItem f) return;

        if (f.TieneUbicacion)
            CentrarEn(f.Latitud, f.Longitud, resolucion: 1.5);

        if (f.TieneFoto)
        {
            ((CollectionView)sender).SelectedItem = null;
            await Shell.Current.GoToAsync($"{nameof(FotoFichajePage)}?archivo={Uri.EscapeDataString(f.Foto!)}" +
                $"&titulo={Uri.EscapeDataString($"{f.NombreCompleto} · {f.TipoFichaje} {f.FechaHora:dd/MM HH:mm}")}");
        }
    }

    // resolución = metros por píxel: 1.5 deja ver una cuadra, 600 todo el país
    private void CentrarEn(double latitud, double longitud, double resolucion)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitud, latitud);
        var punto = new MPoint(x, y);
        Mapa.Map.Navigator.CenterOnAndZoomTo(punto, resolucion, 400);
    }

    private void RedibujarMapa()
    {
        Mapa.Pins.Clear();
        Mapa.Drawables.Clear();

        // Los fichajes sin ubicación (cliente que no la pide) no se dibujan en el mapa
        var items = _viewModel.Items.Where(i => i.TieneUbicacion).ToList();
        if (items.Count == 0) return;

        foreach (var f in items)
        {
            Mapa.Pins.Add(new Pin(Mapa)
            {
                Label = $"{f.NombreCompleto} · {f.FechaHora:HH:mm}",
                Address = $"{f.TipoFichaje} · {f.Lugar}",
                Position = new Position(f.Latitud, f.Longitud),
                Type = PinType.Pin,
                Color = f.TipoFichaje == "Salida" ? ColorSalida : ColorEntrada,
                Scale = 0.9f
            });
            // El globo con el texto por defecto sale enorme: se achica
            var globo = Mapa.Pins[^1].Callout;
            globo.TitleFontSize = 15;
            globo.SubtitleFontSize = 12;
        }

        if (_viewModel.HayRecorrido)
        {
            var linea = new Polyline { StrokeColor = Colors.Blue, StrokeWidth = 4 };
            foreach (var f in items)
                linea.Positions.Add(new Position(f.Latitud, f.Longitud));
            Mapa.Drawables.Add(linea);
        }

        // En una actualización automática no se mueve el mapa: alguien puede estar mirando otra zona
        if (!_viewModel.EncuadrarMapa) return;

        EncuadrarTodos(items);
    }

    // Botón "ver todos": vuelve a mostrar todos los puntos después de mover o acercar el mapa
    private void OnVerTodosClicked(object sender, EventArgs e) =>
        EncuadrarTodos(_viewModel.Items.Where(i => i.TieneUbicacion).ToList());

    private void EncuadrarTodos(List<FichajeItem> items)
    {
        if (items.Count == 0) return;
        if (items.Count == 1)
        {
            CentrarEn(items[0].Latitud, items[0].Longitud, resolucion: 2);
            return;
        }

        // Encuadra todos los puntos con un margen generoso: el dibujo del pin sobresale por arriba de su punto
        var puntos = items.Select(f => SphericalMercator.FromLonLat(f.Longitud, f.Latitud)).ToList();
        var caja = new MRect(puntos.Min(p => p.x), puntos.Min(p => p.y), puntos.Max(p => p.x), puntos.Max(p => p.y));
        Mapa.Map.Navigator.ZoomToBox(caja.Grow(Math.Max(caja.Width, caja.Height) * 0.5 + 200), MBoxFit.Fit, 400);
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
