using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    private readonly EstadoServidor _servidor;
    private readonly IClienteService _clientes;
    private readonly AparienciaService _apariencia;
    private readonly AuthService _auth;
    private readonly PerfilFotos _perfiles;
    private int _toquesEnElLogo;
    private DateTime _primerToque;

    public LoginPage(AuthService authService, EstadoServidor servidor, IClienteService clientes,
        AparienciaService apariencia, IBiometricService biometria, PerfilFotos perfiles)
    {
        InitializeComponent();
        _servidor = servidor;
        _clientes = clientes;
        _apariencia = apariencia;
        _auth = authService;
        _perfiles = perfiles;
        _viewModel = new LoginViewModel(authService, biometria);
        _viewModel.LoginExitoso += OnLoginExitoso;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Limpiar();
        MostrarServidorPersonalizado();
        await _viewModel.ActualizarHuellaAsync();
    }

    // Recordatorio visible solo cuando la app apunta a un servidor distinto del que trae el build
    private void MostrarServidorPersonalizado()
    {
        var url = _clientes.ServidorPersonalizado;
        ServidorLabel.IsVisible = url is not null;
        ServidorLabel.Text = url is null ? string.Empty : $"Servidor: {url}";
    }

    // 5 toques al logo en 3 segundos: pide la dirección del servidor. Sirve para probar con un túnel o con otro servidor
    // usando el mismo APK. Vacía vuelve a la dirección original del build.
    private async void OnLogoTapped(object sender, TappedEventArgs e)
    {
        var ahora = DateTime.UtcNow;
        if (_toquesEnElLogo == 0 || ahora - _primerToque > TimeSpan.FromSeconds(3))
        {
            _toquesEnElLogo = 0;
            _primerToque = ahora;
        }
        if (++_toquesEnElLogo < 5) return;
        _toquesEnElLogo = 0;

        var actual = _clientes.ServidorPersonalizado ?? _clientes.BaseUrl;
        var texto = await Dialogos.Texto("Dirección del servidor",
            $"Pegá la dirección de la API (https://…). Dejala vacía para volver a la original.\n\nAhora: {actual}",
            inicial: _clientes.ServidorPersonalizado, teclado: Keyboard.Url);
        if (texto is null) return; // canceló

        if (texto.Length == 0)
        {
            _clientes.CambiarServidor(null);
            MostrarServidorPersonalizado();
            await Dialogos.Aviso("Listo", "Se volvió a la dirección original de la app.");
            return;
        }

        var (valida, url, mensaje) = DireccionServidor.Normalizar(texto);
        if (!valida) { await Dialogos.Aviso("Dirección no válida", mensaje); return; }

        _clientes.CambiarServidor(url);
        MostrarServidorPersonalizado();

        // Se comprueba al guardar para que el error se vea acá y no recién al intentar entrar
        if (!await _servidor.RespondeAsync())
        {
            await Dialogos.Aviso("Guardada, pero no responde",
                $"Se guardó {url}, pero no se pudo conectar. Revisá que el servidor esté encendido y que la dirección sea correcta.");
            return;
        }

        _ = _apariencia.ActualizarAsync(); // trae los colores y el logo de ese servidor
        await Dialogos.Aviso("Conectado", $"El servidor responde:\n{url}");
    }

    // Tocar "Listo" en el teclado entra, igual que el botón
    private async void OnPinCompleted(object sender, EventArgs e) => await _viewModel.LoginAsync();

    private async void OnOlvidePinClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(OlvidePinPage));

    private async void OnHuellaClicked(object sender, EventArgs e) => await _viewModel.LoginConHuellaAsync();

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await _viewModel.LoginAsync();
    }

    private async void OnLoginExitoso()
    {
        // El supervisor y el administrador entran directo a su panel; desde el menú pueden ir a fichar
        await Shell.Current.GoToAsync(Navegacion.RutaInicial());
        // Primer ingreso sin foto de perfil: se la pide antes de seguir
        await FotoDePerfil.ExigirSiFaltaAsync(_perfiles, _auth);
        if (!SessionManager.Instance.EstaLogueado) await Shell.Current.GoToAsync("//LoginPage");
        else _ = PushDispositivo.PrepararAsync();   // permiso de notificaciones y token para los push
    }
}
