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

    public LoginViewModel(AuthService authService)
    {
        _authService = authService;
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

        Estado = mensaje;
        Procesando = false;

        if (exito)
            LoginExitoso?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
