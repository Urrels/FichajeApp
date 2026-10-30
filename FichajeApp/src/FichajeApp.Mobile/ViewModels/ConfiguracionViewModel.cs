using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Configuración inicial: el cliente conecta su propia planilla de Google. La credencial viaja una sola vez al
// servidor (que la guarda cifrada); el teléfono no se la queda.
public class ConfiguracionViewModel : VmBase
{
    private readonly ConfiguracionApiService _api;

    private string _clave = string.Empty;
    private string _planilla = string.Empty;
    private string _credenciales = string.Empty;
    private string _nombreAdmin = string.Empty;
    private string _emailAdmin = string.Empty;
    private string _pinAdmin = string.Empty;

    private readonly AuthService _auth;

    public ConfiguracionViewModel(ConfiguracionApiService api, AuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    // Por defecto el servidor crea la planilla, la prepara y se la comparte al cliente (todo de una vez)
    private bool _crearPlanilla = true;
    private string _emailPropietario = string.Empty;
    private string _nombreEmpresa = string.Empty;
    private string? _aviso;

    public bool CrearPlanilla
    {
        get => _crearPlanilla;
        set { if (Set(ref _crearPlanilla, value)) { Notificar(nameof(PideLink)); Notificar(nameof(ModoManual)); Notificar(nameof(TextoBotonGuardar)); } }
    }
    public string EmailPropietario { get => _emailPropietario; set => Set(ref _emailPropietario, value); }
    public string NombreEmpresa { get => _nombreEmpresa; set => Set(ref _nombreEmpresa, value); }

    // Si la planilla conectada se borró o dejó de estar compartida, el servidor lo avisa acá
    public string? Aviso { get => _aviso; private set { if (Set(ref _aviso, value)) Notificar(nameof(HayAviso)); } }
    public bool HayAviso => !string.IsNullOrWhiteSpace(Aviso);

    public bool PideLink => !CrearPlanilla;
    public bool ModoManual => ServidorTieneCredencial && !CrearPlanilla;
    public string TextoBotonGuardar => CrearPlanilla ? "Crear y conectar" : "Probar y guardar";

    public string Clave { get => _clave; set => Set(ref _clave, value); }
    public string Planilla { get => _planilla; set => Set(ref _planilla, value); }
    public string Credenciales { get => _credenciales; set { if (Set(ref _credenciales, value)) { Notificar(nameof(CredencialesCargadas)); Notificar(nameof(MuestraEditor)); } } }
    public string NombreAdmin { get => _nombreAdmin; set => Set(ref _nombreAdmin, value); }
    public string EmailAdmin { get => _emailAdmin; set => Set(ref _emailAdmin, value); }
    public string PinAdmin { get => _pinAdmin; set => Set(ref _pinAdmin, value); }

    // No se muestra el contenido de la credencial una vez cargada
    // Un administrador con sesión ya está autorizado; el resto necesita el código de activación
    public bool PideClave => !SessionManager.Instance.TienePermiso(NombresPermisos.CambiarConexion);

    public bool CredencialesCargadas => !string.IsNullOrWhiteSpace(Credenciales);

    // Lo normal: el servidor ya tiene la cuenta de servicio y el cliente solo comparte su planilla con este correo
    private string? _cuentaDeServicio;
    public string? CuentaDeServicio { get => _cuentaDeServicio; private set { if (Set(ref _cuentaDeServicio, value)) { Notificar(nameof(ServidorTieneCredencial)); Notificar(nameof(PideCredencial)); Notificar(nameof(MuestraEditor)); Notificar(nameof(ModoManual)); } } }
    public bool ServidorTieneCredencial => !string.IsNullOrEmpty(CuentaDeServicio);
    public bool PideCredencial => !ServidorTieneCredencial;
    public bool MuestraEditor => PideCredencial && !CredencialesCargadas;

    public async Task CargarEstadoAsync()
    {
        var estado = await _api.EstadoAsync();
        CuentaDeServicio = estado?.CuentaDeServicio;
        Aviso = estado?.Aviso;
    }

    public async Task CopiarCorreoAsync()
    {
        if (string.IsNullOrEmpty(CuentaDeServicio)) return;
        await Clipboard.Default.SetTextAsync(CuentaDeServicio);
        Estado = "Correo copiado. Compartí tu planilla con él como Editor.";
    }

    public event Action? Guardada;

    public async Task ElegirArchivoAsync()
    {
        try
        {
            var archivo = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Elegí el archivo .json de la cuenta de servicio" });
            if (archivo is null) return;

            if (!archivo.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await Dialogos.Aviso("Archivo incorrecto", "Tiene que ser el archivo .json que descargaste de Google Cloud.");
                return;
            }

            await using var flujo = await archivo.OpenReadAsync();
            using var lector = new StreamReader(flujo);
            Credenciales = await lector.ReadToEndAsync();
            Estado = $"Credencial cargada: {archivo.FileName}";
        }
        catch (Exception ex)
        {
            await Dialogos.Aviso("No se pudo leer el archivo", ex.Message);
        }
    }

    public Task GuardarAsync() => ConEspera(async () =>
    {
        if (PideClave && string.IsNullOrWhiteSpace(Clave)) { Estado = "Escribí el código de activación."; return; }
        if (CrearPlanilla)
        {
            if (!EmailPropietario.Contains('@')) { Estado = "Escribí tu cuenta de Google (ahí se te comparte la planilla)."; return; }
            if (string.IsNullOrWhiteSpace(NombreAdmin)) { Estado = "Escribí el nombre del administrador."; return; }
            if (PinAdmin.Length != 4 || !PinAdmin.All(char.IsDigit)) { Estado = "El PIN tiene 4 dígitos."; return; }
        }
        else if (string.IsNullOrWhiteSpace(Planilla)) { Estado = "Pegá el link o el ID de tu planilla."; return; }
        if (PideCredencial && !CredencialesCargadas) { Estado = "Cargá el archivo .json de la cuenta de servicio."; return; }

        Estado = CrearPlanilla
            ? "Creando y preparando tu planilla... puede tardar hasta un minuto."
            : "Conectando y preparando la planilla... puede tardar hasta un minuto.";
        var (exito, mensaje) = await _api.GuardarAsync(new
        {
            Clave = Clave.Trim(),
            Planilla,
            Credenciales,
            CrearPlanillaNueva = CrearPlanilla,
            EmailPropietario = EmailPropietario.Trim(),
            NombreEmpresa = NombreEmpresa.Trim(),
            NombreAdministrador = NombreAdmin,
            EmailAdministrador = EmailAdmin,
            PinAdministrador = PinAdmin
        });

        Estado = mensaje;
        if (!exito)
        {
            // Un cartel, no solo una línea de texto al final de la pantalla (que con el teclado abierto no se ve)
            await Dialogos.Aviso("No se pudo conectar", mensaje);
            return;
        }

        // Ya quedó guardada en el servidor: se limpia lo sensible del teléfono
        Credenciales = string.Empty;
        Clave = string.Empty;
        PinAdmin = string.Empty;
        // Con otra planilla, la sesión de antes ya no vale (esos usuarios no existen ahí): se vuelve a entrar
        var estabaConSesion = SessionManager.Instance.EstaLogueado;
        if (estabaConSesion) _auth.Logout();

        await Dialogos.Aviso("Listo", estabaConSesion
            ? mensaje + "\n\nPor seguridad se cerró tu sesión: entrá con un usuario de esta planilla."
            : mensaje);
        Guardada?.Invoke();
    });
}
