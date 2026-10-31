namespace FichajeApp.Mobile.Views.Componentes;

public partial class EstadoVacio : ContentView
{
    public static readonly BindableProperty IconoProperty = BindableProperty.Create(nameof(Icono), typeof(string), typeof(EstadoVacio), "📭");
    public static readonly BindableProperty TituloProperty = BindableProperty.Create(nameof(Titulo), typeof(string), typeof(EstadoVacio), "No hay nada para mostrar");
    public static readonly BindableProperty MensajeProperty = BindableProperty.Create(nameof(Mensaje), typeof(string), typeof(EstadoVacio), string.Empty);

    public string Icono { get => (string)GetValue(IconoProperty); set => SetValue(IconoProperty, value); }
    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Mensaje { get => (string)GetValue(MensajeProperty); set => SetValue(MensajeProperty, value); }

    public EstadoVacio() => InitializeComponent();
}
