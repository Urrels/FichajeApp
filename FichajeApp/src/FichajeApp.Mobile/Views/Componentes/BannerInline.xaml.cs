using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views.Componentes;

public partial class BannerInline : Border
{
    public static readonly BindableProperty SeveridadProperty = BindableProperty.Create(nameof(Severidad), typeof(Severidad), typeof(BannerInline), Severidad.Info,
        propertyChanged: (b, _, _) => ((BannerInline)b).Pintar());
    public static readonly BindableProperty TextoProperty = BindableProperty.Create(nameof(Texto), typeof(string), typeof(BannerInline), string.Empty,
        propertyChanged: (b, _, nuevo) => ((BannerInline)b).Contenido.Text = nuevo as string);

    // Botón opcional dentro del cartel (por ejemplo "Abrir ajustes" o "Reintentar"): solo se ve si tiene texto
    public static readonly BindableProperty TextoAccionProperty = BindableProperty.Create(nameof(TextoAccion), typeof(string), typeof(BannerInline), null,
        propertyChanged: (b, _, nuevo) => ((BannerInline)b).PintarAccion(nuevo as string));
    public static readonly BindableProperty AccionCommandProperty = BindableProperty.Create(nameof(AccionCommand), typeof(System.Windows.Input.ICommand), typeof(BannerInline), null);

    public Severidad Severidad { get => (Severidad)GetValue(SeveridadProperty); set => SetValue(SeveridadProperty, value); }
    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }
    public string? TextoAccion { get => (string?)GetValue(TextoAccionProperty); set => SetValue(TextoAccionProperty, value); }
    public System.Windows.Input.ICommand? AccionCommand { get => (System.Windows.Input.ICommand?)GetValue(AccionCommandProperty); set => SetValue(AccionCommandProperty, value); }

    private void OnAccionClicked(object? sender, EventArgs e)
    {
        if (AccionCommand?.CanExecute(null) == true) AccionCommand.Execute(null);
    }

    private void PintarAccion(string? texto)
    {
        BotonAccion.Text = texto;
        BotonAccion.IsVisible = !string.IsNullOrWhiteSpace(texto);
    }

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
            Severidad.Aviso => ("⚠", "#F6EBD0", "#9A7B2F", "#5E4A14"),   // ocre apagado, no naranja
            Severidad.Error => ("✖", "#FBE9EA", "#B02A37", "#6E1A22"),
            _ => ("ℹ", "#F6ECE0", "#8A5A2B", "#5C3A18")
        };

        Icono.Text = icono;
        Icono.TextColor = Color.FromArgb(borde);
        Contenido.TextColor = Color.FromArgb(texto);
        BackgroundColor = Color.FromArgb(fondo);
        Stroke = Color.FromArgb(borde);
        // El botón lleva el tono del cartel: texto blanco sobre el color de la severidad
        BotonAccion.BackgroundColor = Color.FromArgb(borde);
        BotonAccion.TextColor = Colors.White;
    }
}
