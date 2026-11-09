using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views.Componentes;

public partial class BannerInline : Border
{
    public static readonly BindableProperty SeveridadProperty = BindableProperty.Create(nameof(Severidad), typeof(Severidad), typeof(BannerInline), Severidad.Info,
        propertyChanged: (b, _, _) => ((BannerInline)b).Pintar());
    public static readonly BindableProperty TextoProperty = BindableProperty.Create(nameof(Texto), typeof(string), typeof(BannerInline), string.Empty,
        propertyChanged: (b, _, nuevo) => ((BannerInline)b).Contenido.Text = nuevo as string);

    public Severidad Severidad { get => (Severidad)GetValue(SeveridadProperty); set => SetValue(SeveridadProperty, value); }
    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    public BannerInline()
    {
        InitializeComponent();
        Pintar();
    }

    // Colores de estado fijos (no dependen de la marca del cliente): fondo suave, borde y texto del mismo tono
    private void Pintar()
    {
        var (icono, fondo, borde, texto) = Severidad switch
        {
            Severidad.Exito => ("✔", "#EEF3E2", "#4E7A2F", "#33501C"),
            Severidad.Aviso => ("⚠", "#FFF1DC", "#B26A00", "#7A4A00"),
            Severidad.Error => ("✖", "#FBE9E4", "#A8322D", "#6E1F1A"),
            _ => ("ℹ", "#F6ECE0", "#8A5A2B", "#5C3A18")
        };

        Icono.Text = icono;
        Icono.TextColor = Color.FromArgb(borde);
        Contenido.TextColor = Color.FromArgb(texto);
        BackgroundColor = Color.FromArgb(fondo);
        Stroke = Color.FromArgb(borde);
    }
}
