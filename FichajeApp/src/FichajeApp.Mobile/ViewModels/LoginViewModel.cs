using System.ComponentModel;
using System.Runtime.CompilerServices;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

public class LoginViewModel : INotifyPropertyChanged
{
    private readonly AuthService _authService;

    private string _email = string.Empty;
    private string _pin = string.Empty;
    private string _estado = string.Empty;
    private bool _procesando;

    private readonly IBiometricService? _biometria;
    private bool _recordar;
    private bool _puedeUsarHuella;
    private bool _huellaDisponible;

    public LoginViewModel(AuthService authService, IBiometricService? biometria = null)
    {
        _authService = authService;
        _biometria = biometria;
    }

    // "Recordar mis datos": guarda email y PIN cifrados para entrar después con la huella
    public bool Recordar
    {
        get => _recordar;
        set { _recordar = value; OnPropertyChanged(); }
    }

    // Se muestra la opción de recordar solo si el teléfono tiene huella, rostro o bloqueo de pantalla
    public bool HuellaDisponible
    {
        get => _huellaDisponible;
        private set { _huellaDisponible = value; OnPropertyChanged(); }
    }

    // Hay datos recordados: se muestra el botón "Entrar con huella"
    public bool PuedeUsarHuella
    {
        get => _puedeUsarHuella;
        private set { _puedeUsarHuella = value; OnPropertyChanged(); }
    }

    public async Task ActualizarHuellaAsync()
    {
        var recordado = await CredencialesRecordadas.ExisteAsync();
        PuedeUsarHuella = recordado;
        Recordar = recordado;
        if (recordado && string.IsNullOrWhiteSpace(Email))
            Email = await CredencialesRecordadas.EmailAsync() ?? string.Empty;
        try { HuellaDisponible = _biometria is not null && await Plugin.Fingerprint.CrossFingerprint.Current.IsAvailableAsync(); }
        catch { HuellaDisponible = false; }
    }

    // Confirma con la huella (o el bloqueo del teléfono) y recién ahí lee los datos guardados
    public async Task LoginConHuellaAsync()
    {
        if (_biometria is null || Procesando) return;
        var (ok, mensaje) = await _biometria.AutenticarAsync("Entrar a la app", "Confirmá que sos vos para ingresar");
        if (!ok) { Estado = "No se pudo confirmar tu identidad. Ingresá con tu PIN."; return; }

        var datos = await CredencialesRecordadas.ObtenerAsync();
        if (datos is null)
        {
            CredencialesRecordadas.Borrar();
            PuedeUsarHuella = false;
            Estado = "Ingresá con tu email y PIN para volver a activarlo.";
            return;
        }

        Procesando = true;
        Estado = "Verificando...";
        var (exito, msg) = await _authService.LoginAsync(datos.Value.Email, datos.Value.Pin);
        Procesando = false;

        if (exito) { Estado = string.Empty; LoginExitoso?.Invoke(); return; }

        // Si el servidor rechazó los datos (PIN cambiado, usuario dado de baja) no se insiste con ellos:
        // reintentar con un PIN viejo sumaría intentos fallidos y bloquearía la cuenta
        if (!_authService.UltimoFalloFueDeConexion)
        {
            CredencialesRecordadas.Borrar();
            PuedeUsarHuella = false;
            Recordar = false;
            Estado = msg + " Ingresá con tu PIN actual.";
        }
        else Estado = msg;
    }

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string Pin
    {
        get => _pin;
        set { _pin = value; OnPropertyChanged(); }
    }

    public string Estado
    {
        get => _estado;
        set { _estado = value; OnPropertyChanged(); OnPropertyChanged(nameof(HayError)); }
    }

    // Lo que dice Estado es un error salvo mientras se está verificando
    public bool HayError => !Procesando && !string.IsNullOrWhiteSpace(Estado);

    public bool Procesando
    {
        get => _procesando;
        set { _procesando = value; OnPropertyChanged(); OnPropertyChanged(nameof(HayError)); }
    }

    public event Action? LoginExitoso;

    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Estado = "Completá email y PIN";
            return;
        }

        Procesando = true;
        Estado = "Verificando...";

        var (exito, mensaje) = await _authService.LoginAsync(Email, Pin);

        // Solo los errores se muestran: "Login exitoso" salía en el cartel rojo de error y, al cerrar sesión, seguía ahí
        Estado = exito ? string.Empty : mensaje;
        Procesando = false;

        if (exito)
        {
            // Solo si la persona lo pidió y el servidor aceptó los datos
            if (Recordar && HuellaDisponible) await CredencialesRecordadas.GuardarAsync(Email.Trim(), Pin);
            else CredencialesRecordadas.Borrar();
            Pin = string.Empty;
            LoginExitoso?.Invoke();
        }
    }

    // Al volver al login (por ejemplo después de cerrar sesión) no tiene que quedar nada de la vez anterior
    public void Limpiar()
    {
        Estado = string.Empty;
        Pin = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
