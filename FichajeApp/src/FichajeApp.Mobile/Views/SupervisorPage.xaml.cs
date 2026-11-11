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
    private static readonly Color ColorEntrada = Color.FromArgb("#4E7A2F");
    private static readonly Color ColorSalida = Color.FromArgb("#8B5A2B");

    private readonly SupervisorViewModel _viewModel;
    private bool _visible;
    private bool _encuadrePendiente;   // los datos llegaron antes de que el mapa tuviera tamaño: se encuadra cuando lo tenga

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
        Mapa.MyLocationEnabled = false;
        Mapa.MyLocationLayer.Enabled = false; // si no, dibuja un punto azul en (0, 0), en medio del océano
        Mapa.SizeChanged += (_, _) => { if (_encuadrePendiente) EsperarMapaYEncuadrar(); };
        Mapa.IsZoomButtonVisible = true;
        // Tocar un pin muestra quién es, a qué hora y dónde
        Mapa.PinClicked += (_, e) =>
        {
            if (e.Pin is null) return;
            e.Pin.ShowCallout();
            e.Handled = true;
        };
        // Antes de que haya fichajes se muestra Argentina entera en vez del océano
        CentrarEn(-34.6037, -58.3816, resolucion: 600, duracion: 0);
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

    private FichajeItem? _elegido;

    // Al elegir un fichaje de la lista, el mapa se centra en ese punto y abajo aparece su detalle
    // (con "Ver en Google Maps" y, si tiene, "Ver foto")
    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FichajeItem f) return;
        _elegido = f;

        if (f.TieneUbicacion)
            CentrarEn(f.Latitud, f.Longitud, resolucion: 1.5);

        DetalleTitulo.Text = $"{f.NombreCompleto} · {f.TipoFichaje}";
        var lugar = string.IsNullOrWhiteSpace(f.Direccion) ? f.Lugar : $"{f.Lugar} · {f.Direccion}";
        DetalleTexto.Text = $"{f.FechaHora:dd/MM/yyyy HH:mm} · {lugar}"
            + (string.IsNullOrWhiteSpace(f.Motivo) ? "" : $"\nMotivo: {f.Motivo}")
            + (f.TieneUbicacion ? "" : "\nEste fichaje no tiene ubicación.");
        MapsButton.IsVisible = f.TieneUbicacion;
        FotoButton.IsVisible = f.TieneFoto;
        // Con un solo botón, que ocupe todo el ancho en vez de quedar a medias
        Grid.SetColumnSpan(MapsButton, f.TieneFoto ? 1 : 2);
        Grid.SetColumnSpan(FotoButton, f.TieneUbicacion ? 1 : 2);
        Grid.SetColumn(FotoButton, f.TieneUbicacion ? 1 : 0);
        DetalleCard.IsVisible = true;
    }

    private void OnCerrarDetalle(object? sender, TappedEventArgs e)
    {
        _elegido = null;
        DetalleCard.IsVisible = false;
    }

    private async void OnVerEnMapsClicked(object? sender, EventArgs e)
    {
        if (_elegido is { TieneUbicacion: true } f) await EnlaceMapas.AbrirAsync(f.Latitud, f.Longitud);
    }

    private async void OnVerFotoClicked(object? sender, EventArgs e)
    {
        if (_elegido is not { TieneFoto: true } f) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        await Shell.Current.GoToAsync($"{nameof(FotoFichajePage)}?archivo={Uri.EscapeDataString(f.Foto!)}" +
            $"&titulo={Uri.EscapeDataString($"{f.NombreCompleto} · {f.TipoFichaje} {f.FechaHora:dd/MM HH:mm}")}" +
            $"&lat={f.Latitud.ToString(inv)}&lon={f.Longitud.ToString(inv)}");
    }

    // resolución = metros por píxel: 1.5 deja ver una cuadra, 600 todo el país
    private void CentrarEn(double latitud, double longitud, double resolucion, int duracion = 400)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitud, latitud);
        var punto = new MPoint(x, y);
        Mapa.Map.Navigator.CenterOnAndZoomTo(punto, resolucion, duracion);
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
            var linea = new Polyline { StrokeColor = Color.FromArgb("#7A2E3A"), StrokeWidth = 4 };
            foreach (var f in items)
                linea.Positions.Add(new Position(f.Latitud, f.Longitud));
            Mapa.Drawables.Add(linea);
        }

        // En una actualización automática no se mueve el mapa: alguien puede estar mirando otra zona
        if (!_viewModel.EncuadrarMapa) return;

        // Mapsui ignora los movimientos que se piden antes de su primer dibujo (pasa al abrir la pantalla con los datos ya listos):
        // se repite el encuadre un instante después, solo si nadie tocó el mapa mientras tanto
        var firma = string.Join("|", items.Select(i => $"{i.Latitud},{i.Longitud}"));
        foreach (var espera in new[] { 700, 1800 })
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(espera), () =>
            {
                if (_visible && _viewModel.EncuadrarMapa && firma == string.Join("|", ConUbicacion().Select(i => $"{i.Latitud},{i.Longitud}")))
                    EncuadrarTodos(ConUbicacion());
            });

        EncuadrarTodos(items);
    }

    // Botón "ver todos": vuelve a mostrar todos los puntos después de mover o acercar el mapa
    private void OnVerTodosClicked(object sender, EventArgs e) =>
        EncuadrarTodos(ConUbicacion());

    // El recorrido que guardó el teléfono de la persona elegida en el filtro, el día "Desde"
    private async void OnVerRecorridoClicked(object sender, EventArgs e)
    {
        var persona = _viewModel.EmpleadoSeleccionado;
        if (string.IsNullOrEmpty(persona) || persona == "Todos")
        {
            await Dialogos.Aviso("Elegí una persona", "Elegí en el filtro Empleado a quién querés ver el recorrido.");
            return;
        }
        await Shell.Current.GoToAsync($"{nameof(RecorridoPage)}?empleado={Uri.EscapeDataString(persona)}&fecha={_viewModel.Desde:yyyy-MM-dd}");
    }

    // El control de Mapsui toma su tamaño unos instantes después que la pantalla; si los datos llegan antes, se espera (hasta 6 s)
    private void EsperarMapaYEncuadrar()
    {
        var intentos = 0;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(200), () =>
        {
            if (!_encuadrePendiente) return false;
            if (Mapa.Map.Navigator.Viewport.Width > 0)
            {
                _encuadrePendiente = false;
                EncuadrarTodos(ConUbicacion());
                return false;
            }
            return ++intentos < 30;
        });
    }

    private List<FichajeItem> ConUbicacion() => _viewModel.Items.Where(i => i.TieneUbicacion).ToList();

    private void EncuadrarTodos(List<FichajeItem> items)
    {
        if (items.Count == 0) return;
        if (Mapa.Map.Navigator.Viewport.Width <= 0) { _encuadrePendiente = true; EsperarMapaYEncuadrar(); return; }
        if (items.Count == 1)
        {
            CentrarEn(items[0].Latitud, items[0].Longitud, resolucion: 2);
            return;
        }

        // Encuadra todos los puntos con un margen generoso: el dibujo del pin sobresale por arriba de su punto
        var puntos = items.Select(f => SphericalMercator.FromLonLat(f.Longitud, f.Latitud)).ToList();
        var caja = new MRect(puntos.Min(p => p.x), puntos.Min(p => p.y), puntos.Max(p => p.x), puntos.Max(p => p.y));
        // Se calcula a mano la resolución (metros por píxel) que hace entrar todos los puntos: ZoomToBox no hacía nada con puntos muy cercanos
        var vista = Mapa.Map.Navigator.Viewport;
        var resolucion = Math.Max(caja.Width / vista.Width, caja.Height / vista.Height) * 2.3; // 2,3 = margen: el dibujo del pin sobresale por arriba de su punto
        Mapa.Map.Navigator.CenterOnAndZoomTo(caja.Centroid, Math.Max(resolucion, 2), 400);
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
