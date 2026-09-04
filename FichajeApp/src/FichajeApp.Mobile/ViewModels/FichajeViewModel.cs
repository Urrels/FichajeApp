using System.ComponentModel;
using System.Runtime.CompilerServices;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Implementa ISessionObserver: se suscribe al Singleton SessionManager para
// reaccionar automáticamente cuando la sesión finaliza (ej. token vencido).
public class FichajeViewModel : INotifyPropertyChanged, ISessionObserver
{
    private readonly GeolocationService _geoService;
    private readonly BiometricService _biometricService;
    private readonly ApiService _apiService;

    private string _nombreCompleto = string.Empty;
    private string _estado = "Listo para fichar";
    private bool _procesando;

    public event Action? SesionFinalizada;

    public FichajeViewModel(ApiService apiService)
    {
        _geoService = new GeolocationService();
        _biometricService = new BiometricService();
        _apiService = apiService;

        SessionManager.Instance.Suscribir(this);
        NombreCompleto = SessionManager.Instance.NombreCompleto ?? string.Empty;
    }

    public string NombreCompleto
    {
        get => _nombreCompleto;
        private set { _nombreCompleto = value; OnPropertyChanged(); }
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

    public async Task FicharAsync()
    {
        Procesando = true;

        Estado = "Obteniendo ubicación...";
        var (geoExito, lat, lon, geoMensaje) = await _geoService.ObtenerUbicacionAsync();
        if (!geoExito) { Estado = geoMensaje; Procesando = false; return; }

        Estado = "Verificá tu identidad...";
        var (bioExito, bioMensaje) = await _biometricService.AutenticarAsync();
        if (!bioExito) { Estado = bioMensaje; Procesando = false; return; }

        Estado = "Registrando fichaje...";
        var fichaje = new FichajeRequest { Latitud = lat, Longitud = lon };
        var (apiExito, apiMensaje) = await _apiService.RegistrarFichajeAsync(fichaje);

        Estado = apiMensaje;
        Procesando = false;
    }

    // --- ISessionObserver ---
    public void OnSesionIniciada(string nombreCompleto)
    {
        NombreCompleto = nombreCompleto;
    }

    public void OnSesionFinalizada()
    {
        NombreCompleto = string.Empty;
        Estado = "Sesión finalizada";
        SesionFinalizada?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
