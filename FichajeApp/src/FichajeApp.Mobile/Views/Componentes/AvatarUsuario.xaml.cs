using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views.Componentes;

// Avatar de una persona: su foto de perfil si tiene y, si no, las iniciales
public partial class AvatarUsuario : Grid
{
    public static readonly BindableProperty NombreProperty = BindableProperty.Create(nameof(Nombre), typeof(string), typeof(AvatarUsuario), string.Empty,
        propertyChanged: (b, _, _) => ((AvatarUsuario)b).PintarIniciales());
    public static readonly BindableProperty EmpleadoIdProperty = BindableProperty.Create(nameof(EmpleadoId), typeof(int), typeof(AvatarUsuario), 0,
        propertyChanged: (b, _, _) => _ = ((AvatarUsuario)b).CargarFotoAsync());
    public static readonly BindableProperty TamanoProperty = BindableProperty.Create(nameof(Tamano), typeof(double), typeof(AvatarUsuario), 44d,
        propertyChanged: (b, _, nuevo) => ((AvatarUsuario)b).Dimensionar((double)nuevo));

    public string Nombre { get => (string)GetValue(NombreProperty); set => SetValue(NombreProperty, value); }
    public int EmpleadoId { get => (int)GetValue(EmpleadoIdProperty); set => SetValue(EmpleadoIdProperty, value); }
    public double Tamano { get => (double)GetValue(TamanoProperty); set => SetValue(TamanoProperty, value); }

    private PerfilFotos? _fotos;
    private int _version;   // descarta respuestas viejas: una celda reciclada puede cambiar de persona mientras se baja la foto

    public AvatarUsuario()
    {
        InitializeComponent();
        Dimensionar(Tamano);
        // Mientras está en pantalla escucha los cambios de foto; al salir deja de escuchar
        Loaded += (_, _) => { Servicio.Cambio += AlCambiarUnaFoto; _ = CargarFotoAsync(); };
        Unloaded += (_, _) => { if (_fotos is not null) _fotos.Cambio -= AlCambiarUnaFoto; };
    }

    private PerfilFotos Servicio => _fotos ??= Application.Current?.Handler?.MauiContext?.Services.GetService<PerfilFotos>()!;

    private void AlCambiarUnaFoto() => MainThread.BeginInvokeOnMainThread(() => _ = CargarFotoAsync());

    private void Dimensionar(double lado)
    {
        WidthRequest = HeightRequest = lado;
        IniciallesLabel.FontSize = lado * 0.36;
    }

    private void PintarIniciales() =>
        IniciallesLabel.Text = string.Concat((Nombre ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(p => char.ToUpperInvariant(p[0])));

    private async Task CargarFotoAsync()
    {
        var mia = ++_version;
        if (EmpleadoId <= 0 || Servicio is null) { MarcoFoto.IsVisible = false; return; }

        var ruta = await Servicio.RutaAsync(EmpleadoId);
        if (mia != _version) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ruta is null) { MarcoFoto.IsVisible = false; Foto.Source = null; return; }
            Foto.Source = ImageSource.FromFile(ruta);
            MarcoFoto.IsVisible = true;
        });
    }
}
