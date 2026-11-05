using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views.Componentes;

public partial class SelectorDeColor : ContentView
{
    // El color elegido, "#RRGGBB". Se actualiza mientras se arrastra (enlace en los dos sentidos).
    public static readonly BindableProperty HexProperty = BindableProperty.Create(nameof(Hex), typeof(string), typeof(SelectorDeColor), "#808080",
        BindingMode.TwoWay, propertyChanged: (b, _, nuevo) => ((SelectorDeColor)b).AlCambiarHex((string?)nuevo));

    public string Hex { get => (string)GetValue(HexProperty); set => SetValue(HexProperty, value); }

    private readonly DibujoDelCuadro _cuadro = new();
    private readonly DibujoDelTono _tono = new();
    private double _h, _s = 1, _v = 1;
    private bool _interno;   // lo que se cambia desde acá no tiene que volver a entrar por AlCambiarHex

    public SelectorDeColor()
    {
        InitializeComponent();
        Cuadro.Drawable = _cuadro;
        Tono.Drawable = _tono;
        ArmarSugeridos();
        EvitarQueLaPaginaSeDesplace(Cuadro);
        EvitarQueLaPaginaSeDesplace(Tono);
        AlCambiarHex(Hex);
    }

    // ---- Arrastre ----

    private void OnCuadro(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || Cuadro.Width <= 0 || Cuadro.Height <= 0) return;
        var p = e.Touches[0];
        _s = Math.Clamp(p.X / Cuadro.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / Cuadro.Height, 0, 1);
        Aplicar();
    }

    private void OnTono(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || Tono.Width <= 0) return;
        var p = e.Touches[0];
        _h = Math.Clamp(p.X / Tono.Width, 0, 1) * 360;
        Aplicar();
    }

    private void OnCuadroFin(object? sender, TouchEventArgs e) => Hapticos();
    private void OnTonoFin(object? sender, TouchEventArgs e) => Hapticos();

    private static void Hapticos()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { /* sin vibración en este teléfono */ }
    }

    private void Aplicar()
    {
        var hex = ColorHsv.AHex(_h, _s, _v);
        _interno = true;
        Hex = hex;
        _interno = false;
        Redibujar(hex);
    }

    // ---- Cuando el color cambia desde afuera (otro color elegido, un sugerido, el código escrito) ----

    private void AlCambiarHex(string? valor)
    {
        if (_interno) return;
        var hex = ColoresApp.Normalizar(valor);
        if (hex is null) return;

        var (h, s, v) = ColorHsv.Desde(hex);
        // En los grises y el negro el tono no está definido: se conserva el que había para que la barra no salte al rojo
        if (s > 0 && v > 0) _h = h;
        _s = s;
        _v = v;
        Redibujar(hex);
    }

    private void Redibujar(string hex)
    {
        _cuadro.H = _h; _cuadro.S = _s; _cuadro.V = _v;
        _tono.H = _h; _tono.Color = Color.FromArgb(hex);
        Cuadro.Invalidate();
        Tono.Invalidate();

        Muestra.BackgroundColor = Color.FromArgb(hex);
        if (!CodigoEntry.IsFocused && CodigoEntry.Text != hex) CodigoEntry.Text = hex;
    }

    // ---- Código escrito a mano ----

    private void OnCodigoCambio(object? sender, TextChangedEventArgs e)
    {
        if (ColoresApp.Normalizar(e.NewTextValue) is { } hex && hex != Hex) Hex = hex;
    }

    private void OnCodigoListo(object? sender, EventArgs e)
    {
        // Al terminar de escribir se muestra el código ya normalizado (con # y en mayúsculas)
        CodigoEntry.Text = ColoresApp.Normalizar(CodigoEntry.Text) ?? Hex;
    }

    // ---- Colores sugeridos ----

    private void ArmarSugeridos()
    {
        foreach (var (nombre, hex) in ColoresApp.Paleta)
        {
            var muestra = new Border
            {
                WidthRequest = 34,
                HeightRequest = 34,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 17 },
                BackgroundColor = Color.FromArgb(hex),
                Stroke = Color.FromArgb("#33000000"),
                StrokeThickness = 1
            };
            SemanticProperties.SetDescription(muestra, nombre);
            var toque = new TapGestureRecognizer();
            toque.Tapped += (_, _) => { Hex = hex; Hapticos(); };
            muestra.GestureRecognizers.Add(toque);
            Sugeridos.Children.Add(muestra);
        }
    }

    // La página entera se desplaza hacia arriba y abajo: sin esto, arrastrar el dedo por el cuadro movería la página en vez del color
    private static void EvitarQueLaPaginaSeDesplace(GraphicsView vista)
    {
        vista.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (vista.Handler?.PlatformView is Android.Views.View nativa)
            {
                nativa.Touch += (_, e) =>
                {
                    var accion = e.Event?.Action;
                    var padre = nativa.Parent;
                    if (accion == Android.Views.MotionEventActions.Down || accion == Android.Views.MotionEventActions.Move)
                        padre?.RequestDisallowInterceptTouchEvent(true);
                    else if (accion == Android.Views.MotionEventActions.Up || accion == Android.Views.MotionEventActions.Cancel)
                        padre?.RequestDisallowInterceptTouchEvent(false);
                    e.Handled = false;   // el control sigue recibiendo el toque como siempre
                };
            }
#endif
        };
    }
}

// Cuadro: de izquierda (pálido) a derecha (intenso) y de arriba (claro) a abajo (oscuro), con el tono elegido
public class DibujoDelCuadro : IDrawable
{
    public double H, S, V = 1;

    public void Draw(ICanvas canvas, RectF area)
    {
        canvas.SaveState();
        var recorte = new PathF();
        recorte.AppendRoundedRectangle(area, 12);
        canvas.ClipPath(recorte);

        var tono = Color.FromArgb(ColorHsv.AHex(H, 1, 1));
        canvas.SetFillPaint(new LinearGradientPaint(
            new[] { new PaintGradientStop(0, Colors.White), new PaintGradientStop(1, tono) }, new Point(0, 0), new Point(1, 0)), area);
        canvas.FillRectangle(area);
        canvas.SetFillPaint(new LinearGradientPaint(
            new[] { new PaintGradientStop(0, Colors.Transparent), new PaintGradientStop(1, Colors.Black) }, new Point(0, 0), new Point(0, 1)), area);
        canvas.FillRectangle(area);
        canvas.RestoreState();

        // El círculo que muestra dónde está el color elegido
        var x = Math.Clamp((float)(S * area.Width), 13f, area.Width - 13f);
        var y = Math.Clamp((float)((1 - V) * area.Height), 13f, area.Height - 13f);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#66000000");
        canvas.DrawCircle(x, y, 13);
        canvas.StrokeSize = 3;
        canvas.StrokeColor = Colors.White;
        canvas.DrawCircle(x, y, 11);
    }
}

// Barra de tono: el arcoíris completo; el círculo muestra el tono elegido con el color actual adentro
public class DibujoDelTono : IDrawable
{
    public double H;
    public Color Color = Colors.Gray;

    public void Draw(ICanvas canvas, RectF area)
    {
        var barra = new RectF(area.X, area.Y + area.Height * 0.25f, area.Width, area.Height * 0.5f);
        var paradas = Enumerable.Range(0, 7)
            .Select(i => new PaintGradientStop(i / 6f, Color.FromArgb(ColorHsv.AHex(i * 60, 1, 1)))).ToArray();
        canvas.SetFillPaint(new LinearGradientPaint(paradas, new Point(0, 0), new Point(1, 0)), barra);
        canvas.FillRoundedRectangle(barra, barra.Height / 2);

        var x = Math.Clamp((float)(H / 360 * area.Width), 14, area.Width - 14);
        var y = area.Y + area.Height / 2;
        canvas.FillColor = Color;
        canvas.FillCircle(x, y, 13);
        canvas.StrokeSize = 3;
        canvas.StrokeColor = Colors.White;
        canvas.DrawCircle(x, y, 13);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#66000000");
        canvas.DrawCircle(x, y, 15);
    }
}
