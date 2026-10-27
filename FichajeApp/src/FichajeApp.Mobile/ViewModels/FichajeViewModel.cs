using System.ComponentModel;
using System.Runtime.CompilerServices;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Implementa ISessionObserver: se suscribe al Singleton SessionManager para
// reaccionar automáticamente cuando la sesión finaliza (ej. token vencido).
public class FichajeViewModel : INotifyPropertyChanged, ISessionObserver
{
    private readonly IGeolocationService _geoService;
    private readonly IBiometricService _biometricService;
    private readonly ApiService _apiService;

    private string _nombreCompleto = string.Empty;
    private string _estado = "Listo para fichar";
    private bool _procesando;

    public event Action? SesionFinalizada;

    public FichajeViewModel(ApiService apiService, IGeolocationService geoService, IBiometricService biometricService)
    {
        _geoService = geoService;
        _biometricService = biometricService;
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

        // El permiso se resuelve primero y solo, para no superponer diálogos del sistema.
        Estado = "Obteniendo ubicación...";
        if (!await _geoService.PedirPermisoAsync())
        {
            Estado = "Permiso de ubicación denegado";
            Procesando = false;
            return;
        }

        // Ubicación y biometría corren en paralelo: el GPS se obtiene mientras el usuario se autentica.
        var ubicacionTask = _geoService.ObtenerUbicacionAsync();
        Estado = "Verificá tu identidad...";
        var (bioExito, bioMensaje) = await _biometricService.AutenticarAsync();
        var ubicacion = await ubicacionTask;

        if (!bioExito) { Estado = bioMensaje; Procesando = false; return; }
        if (!ubicacion.Exito) { Estado = ubicacion.Mensaje; Procesando = false; return; }

        Estado = "Registrando fichaje...";
        var fichaje = new FichajeRequest
        {
            Latitud = ubicacion.Lat,
            Longitud = ubicacion.Lon,
            Precision = ubicacion.Precision,
            EsUbicacionFalsa = ubicacion.EsFalsa,
            Direccion = ubicacion.Direccion
        };
        var resultado = await _apiService.RegistrarFichajeAsync(fichaje);

        if (resultado.RequiereMotivo)
        {
            // Mismo request: no se vuelve a pedir ubicación ni biometría
            var motivo = await Shell.Current.DisplayPromptAsync(
                "Fichaje fuera de un lugar conocido",
                "Indicá el motivo (ej. visita cliente X)",
                maxLength: 120);

            if (string.IsNullOrWhiteSpace(motivo))
            {
                Estado = "Fichaje cancelado: falta el motivo";
                Procesando = false;
                return;
            }

            fichaje.Motivo = motivo.Trim();
            Estado = "Registrando fichaje...";
            resultado = await _apiService.RegistrarFichajeAsync(fichaje);
        }

        Estado = resultado.Mensaje;
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
