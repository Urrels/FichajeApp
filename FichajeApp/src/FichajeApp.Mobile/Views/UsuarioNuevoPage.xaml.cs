using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Alta de usuario en una sola pantalla: si algo falla (PIN débil, email repetido, sin conexión) los datos
// quedan escritos y solo se corrige lo que hace falta. Antes eran 4 diálogos seguidos que se perdían al fallar.
public partial class UsuarioNuevoPage : ContentPage
{
    private readonly AdminApiService _api;
    private readonly Dictionary<int, CheckBox> _roles = new();
    private readonly Dictionary<int, string> _nombresRoles = new();

    public UsuarioNuevoPage(AdminApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_roles.Count > 0) return;   // ya cargados: no pisar lo que el usuario marcó

        var r = await _api.ListarRolesParaAsignar();
        if (!r.Exito || r.Datos is null) { Mostrar(r.Mensaje); return; }

        foreach (var rol in r.Datos)
        {
            var check = new CheckBox { IsChecked = rol.Nombre == "Empleado", VerticalOptions = LayoutOptions.Center };
            _roles[rol.Id] = check;
            _nombresRoles[rol.Id] = rol.Nombre;
            var fila = new HorizontalStackLayout { Spacing = 8 };
            fila.Add(check);
            fila.Add(new Label { Text = rol.Nombre, VerticalOptions = LayoutOptions.Center });
            fila.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => check.IsChecked = !check.IsChecked) });
            RolesLista.Add(fila);
        }
    }

    private void Mostrar(string mensaje)
    {
        Aviso.Texto = mensaje;
        Aviso.IsVisible = true;
    }

    private async void OnRevisar(object sender, EventArgs e)
    {
        var nombre = NombreEntry.Text?.Trim() ?? "";
        var email = EmailEntry.Text?.Trim() ?? "";
        var pin = PinEntry.Text ?? "";
        var rolIds = _roles.Where(x => x.Value.IsChecked).Select(x => x.Key).ToList();

        if (nombre.Length == 0 || email.Length == 0 || pin.Length != 4)
        {
            Mostrar("Completá el nombre, el email y un PIN de 4 dígitos.");
            return;
        }
        if (rolIds.Count == 0)
        {
            Mostrar("Elegí al menos un rol.");
            return;
        }

        // Vista previa antes de crear
        var roles = string.Join(", ", rolIds.Select(id => _nombresRoles[id]));
        var ok = await Dialogos.Confirmar("Revisá los datos",
            $"Nombre: {nombre}\nEmail: {email}\nRoles: {roles}\nPIN: ••••", "Crear usuario");
        if (!ok) return;

        Aviso.IsVisible = false;
        CrearButton.IsEnabled = false;
        Espera.IsVisible = Espera.IsRunning = true;
        var r = await _api.CrearUsuario(nombre, email, pin, rolIds);
        Espera.IsVisible = Espera.IsRunning = false;
        CrearButton.IsEnabled = true;

        if (!r.Exito)
        {
            Mostrar(r.Mensaje);   // los campos NO se borran
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private async void OnCancelar(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
