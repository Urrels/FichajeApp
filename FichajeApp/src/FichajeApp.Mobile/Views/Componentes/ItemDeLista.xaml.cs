namespace FichajeApp.Mobile.Views.Componentes;

public partial class ItemDeLista : Border
{
    public static readonly BindableProperty TituloProperty = BindableProperty.Create(nameof(Titulo), typeof(string), typeof(ItemDeLista), string.Empty,
        propertyChanged: (b, _, _) => ((ItemDeLista)b).OnPropertyChanged(nameof(Iniciales)));
    public static readonly BindableProperty DetalleProperty = BindableProperty.Create(nameof(Detalle), typeof(string), typeof(ItemDeLista), string.Empty,
        propertyChanged: (b, _, _) => ((ItemDeLista)b).OnPropertyChanged(nameof(TieneDetalle)));
    public static readonly BindableProperty LateralProperty = BindableProperty.Create(nameof(Lateral), typeof(string), typeof(ItemDeLista), string.Empty,
        propertyChanged: (b, _, _) => ((ItemDeLista)b).OnPropertyChanged(nameof(TieneLateral)));
    public static readonly BindableProperty ConAvatarProperty = BindableProperty.Create(nameof(ConAvatar), typeof(bool), typeof(ItemDeLista), true);

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Detalle { get => (string)GetValue(DetalleProperty); set => SetValue(DetalleProperty, value); }
    public string Lateral { get => (string)GetValue(LateralProperty); set => SetValue(LateralProperty, value); }
    public bool ConAvatar { get => (bool)GetValue(ConAvatarProperty); set => SetValue(ConAvatarProperty, value); }

    public bool TieneDetalle => !string.IsNullOrWhiteSpace(Detalle);
    public bool TieneLateral => !string.IsNullOrWhiteSpace(Lateral);

    // Las dos primeras letras de las dos primeras palabras
    public string Iniciales => string.Concat((Titulo ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public ItemDeLista() => InitializeComponent();
}
