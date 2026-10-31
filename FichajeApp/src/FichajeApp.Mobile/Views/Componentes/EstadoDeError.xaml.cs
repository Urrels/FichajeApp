namespace FichajeApp.Mobile.Views.Componentes;

public partial class EstadoDeError : ContentView
{
    public static readonly BindableProperty IconoProperty = BindableProperty.Create(nameof(Icono), typeof(string), typeof(EstadoDeError), "⚠️");
    public static readonly BindableProperty TituloProperty = BindableProperty.Create(nameof(Titulo), typeof(string), typeof(EstadoDeError), "Algo salió mal");
    public static readonly BindableProperty MensajeProperty = BindableProperty.Create(nameof(Mensaje), typeof(string), typeof(EstadoDeError), string.Empty);
    public static readonly BindableProperty TextoBotonProperty = BindableProperty.Create(nameof(TextoBoton), typeof(string), typeof(EstadoDeError), "Reintentar",
        propertyChanged: (b, _, _) => ((EstadoDeError)b).OnPropertyChanged(nameof(TieneBoton)));

    public string Icono { get => (string)GetValue(IconoProperty); set => SetValue(IconoProperty, value); }
    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Mensaje { get => (string)GetValue(MensajeProperty); set => SetValue(MensajeProperty, value); }
    public string TextoBoton { get => (string)GetValue(TextoBotonProperty); set => SetValue(TextoBotonProperty, value); }

    // Sin texto de botón no se muestra (por ejemplo, un error que solo se resuelve cambiando algo afuera)
    public bool TieneBoton => !string.IsNullOrWhiteSpace(TextoBoton);

    // La pantalla que lo usa decide qué hace "Reintentar"
    public event EventHandler? Reintentar;

    public EstadoDeError() => InitializeComponent();

    private void OnBoton(object? sender, EventArgs e) => Reintentar?.Invoke(this, EventArgs.Empty);
}
