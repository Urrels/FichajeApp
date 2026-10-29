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
    private bool _mostrarMapa;
    private string? _enlaceMaps;

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

    public bool MostrarMapa
    {
        get => _mostrarMapa;
        private set { _mostrarMapa = value; OnPropertyChanged(); }
    }

    // Enlace a Google Maps del lugar donde se ficó
    private void MostrarUbicacion(double lat, double lon)
    {
        _enlaceMaps = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"https://www.google.com/maps?q={lat},{lon}");
        MostrarMapa = true;
    }

    public async Task AbrirEnMapsAsync()
    {
        if (_enlaceMaps is null) return;
        try { await Launcher.Default.OpenAsync(new Uri(_enlaceMaps)); }
        catch { Estado = "No se pudo abrir Google Maps"; }
    }

    public async Task FicharAsync()
    {
        Procesando = true;
        MostrarMapa = false;

        Estado = "Obteniendo ubicación...";
        var ubicacion = await _geoService.ObtenerUbicacionAsync();
        if (!ubicacion.Exito) { Estado = ubicacion.Mensaje; Procesando = false; return; }

        Estado = "Verificá tu identidad...";
        var (bioExito, bioMensaje) = await _biometricService.AutenticarAsync();
        if (!bioExito) { Estado = bioMensaje; Procesando = false; return; }

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
        if (resultado.Exito)
            MostrarUbicacion(ubicacion.Lat, ubicacion.Lon);
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
