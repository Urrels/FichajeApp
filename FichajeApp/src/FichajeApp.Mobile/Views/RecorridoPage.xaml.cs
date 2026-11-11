using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Recorrido de una persona en un día: los puntos que su teléfono guardó mientras tenía la jornada abierta, unidos por una línea.
[QueryProperty(nameof(Empleado), "empleado")]
[QueryProperty(nameof(Fecha), "fecha")]
public partial class RecorridoPage : ContentPage
{
    private readonly ApiService _api;
    private bool _cargado;

    public RecorridoPage(ApiService api)
    {
        InitializeComponent();
        _api = api;

        Mapa.Map.Layers.Insert(0, OpenStreetMap.CreateTileLayer("FichajeApp-recorrido"));
        Mapa.IsMyLocationButtonVisible = false;
        Mapa.MyLocationEnabled = false;
        Mapa.MyLocationLayer.Enabled = false;
        Mapa.IsZoomButtonVisible = true;
        Mapa.PinClicked += (_, e) => { e.Pin?.ShowCallout(); if (e.Pin is not null) e.Handled = true; };
    }

    public string Empleado { get; set; } = "";
    public string Fecha { get; set; } = "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_cargado) return;

        var nombre = Uri.UnescapeDataString(Empleado);
        var dia = DateTime.TryParse(Fecha, out var f) ? f.Date : DateTime.Today;
        Titulo.Text = $"{nombre} · {dia:dd/MM/yyyy}";

        var r = await _api.ObtenerRecorridoAsync(nombre, dia);
        if (!r.Exito || r.Datos is null)
        {
            Aviso.Texto = r.Mensaje;
            Aviso.IsVisible = true;
            return;
        }
        _cargado = true;

        if (r.Datos.Count == 0)
        {
            Aviso.Texto = "No hay recorrido guardado de ese día. Solo se registra si la empresa lo activó y mientras la jornada está abierta.";
            Aviso.IsVisible = true;
            return;
        }

        Dibujar(r.Datos);
    }

    private void Dibujar(List<PuntoRecorridoItem> puntos)
    {
        Mapa.Pins.Clear();
        Mapa.Drawables.Clear();

        var linea = new Polyline { StrokeColor = Color.FromArgb("#7A2E3A"), StrokeWidth = 4 };
        for (int i = 0; i < puntos.Count; i++)
        {
            var p = puntos[i];
            linea.Positions.Add(new Position(p.Latitud, p.Longitud));

            var primero = i == 0;
            var ultimo = i == puntos.Count - 1;
            Mapa.Pins.Add(new Pin(Mapa)
            {
                Label = $"{p.FechaHora:HH:mm}",
                Address = primero ? "Primer punto" : ultimo ? "Último punto" : "Punto del recorrido",
                Position = new Position(p.Latitud, p.Longitud),
                Type = PinType.Pin,
                Color = primero ? Color.FromArgb("#4E7A2F") : ultimo ? Color.FromArgb("#8B5A2B") : Color.FromArgb("#7A2E3A"),
                Scale = primero || ultimo ? 0.9f : 0.6f
            });
            var globo = Mapa.Pins[^1].Callout;
            globo.TitleFontSize = 15;
            globo.SubtitleFontSize = 12;
        }
        Mapa.Drawables.Add(linea);

        // Mapsui ignora los movimientos pedidos antes de su primer dibujo: se encuadra un instante después y otra vez más tarde
        foreach (var espera in new[] { 300, 900, 1800 })
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(espera), () => Encuadrar(puntos));
    }

    private void Encuadrar(List<PuntoRecorridoItem> puntos)
    {
        var vista = Mapa.Map.Navigator.Viewport;
        if (vista.Width <= 0) return;

        var xy = puntos.Select(p => SphericalMercator.FromLonLat(p.Longitud, p.Latitud)).ToList();
        var caja = new MRect(xy.Min(p => p.x), xy.Min(p => p.y), xy.Max(p => p.x), xy.Max(p => p.y));
        var resolucion = Math.Max(caja.Width / vista.Width, caja.Height / vista.Height) * 2.3;
        Mapa.Map.Navigator.CenterOnAndZoomTo(caja.Centroid, Math.Max(resolucion, 2), 400);
    }
}
