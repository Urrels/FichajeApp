namespace FichajeApp.Mobile.Views.Componentes;

public partial class OpcionDeMenu : Border
{
    public static readonly BindableProperty TituloProperty = BindableProperty.Create(nameof(Titulo), typeof(string), typeof(OpcionDeMenu), string.Empty);
    public static readonly BindableProperty DescripcionProperty = BindableProperty.Create(nameof(Descripcion), typeof(string), typeof(OpcionDeMenu), string.Empty,
        propertyChanged: (b, _, _) => ((OpcionDeMenu)b).OnPropertyChanged(nameof(TieneDescripcion)));
    public static readonly BindableProperty ValorProperty = BindableProperty.Create(nameof(Valor), typeof(string), typeof(OpcionDeMenu), string.Empty,
        propertyChanged: (b, _, _) => ((OpcionDeMenu)b).OnPropertyChanged(nameof(TieneValor)));

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Descripcion { get => (string)GetValue(DescripcionProperty); set => SetValue(DescripcionProperty, value); }

    // Para las opciones que muestran su valor actual ("Ubicación: Libre")
    public string Valor { get => (string)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }

    public bool TieneDescripcion => !string.IsNullOrWhiteSpace(Descripcion);
    public bool TieneValor => !string.IsNullOrWhiteSpace(Valor);

    public event EventHandler? Tocado;

    public OpcionDeMenu() => InitializeComponent();

    // Un parpadeo corto confirma el toque
    private async void OnToque(object? sender, TappedEventArgs e)
    {
        await this.FadeTo(0.55, 60);
        await this.FadeTo(1, 120);
        Tocado?.Invoke(this, EventArgs.Empty);
    }
}
