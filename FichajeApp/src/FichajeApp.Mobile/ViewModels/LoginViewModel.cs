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

    private string _codigoEmpresa = string.Empty;

    public LoginViewModel(AuthService authService, IClienteService clientes)
    {
        _authService = authService;
        // Precarga el código usado en el último login exitoso
        _codigoEmpresa = clientes.Codigo ?? string.Empty;
    }

    public string CodigoEmpresa
    {
        get => _codigoEmpresa;
        set { _codigoEmpresa = value; OnPropertyChanged(); }
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
        set { _estado = value; OnPropertyChanged(); }
    }

    public bool Procesando
    {
        get => _procesando;
        set { _procesando = value; OnPropertyChanged(); }
    }

    public event Action? LoginExitoso;

    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(CodigoEmpresa) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pin))
        {
            Estado = "Completá código de empresa, email y PIN";
            return;
        }

        Procesando = true;
        Estado = "Verificando...";

        var (exito, mensaje) = await _authService.LoginAsync(CodigoEmpresa, Email, Pin);

        Estado = mensaje;
        Procesando = false;

        if (exito)
            LoginExitoso?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
