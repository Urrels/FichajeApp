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
    private bool _mostrarMapa;
    private string? _enlaceMaps;
    private string? _proximo;

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

    // Lo que va a ser el próximo fichaje: se muestra en el botón para saber si se está entrando o saliendo
    public string TextoBoton => _proximo is null ? "Fichar" : $"Fichar {_proximo.ToLowerInvariant()}";

    public async Task ActualizarProximoAsync()
    {
        _proximo = await _apiService.ObtenerProximoTipoAsync() ?? _proximo;
        OnPropertyChanged(nameof(TextoBoton));
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

        // Cómo ficha este cliente: con o sin ubicación, con o sin foto
        var config = await _apiService.ObtenerConfiguracionFichajeAsync();

        // El permiso se resuelve primero y solo, para no superponer diálogos del sistema.
        if (config.PideUbicacion)
        {
            Estado = "Obteniendo ubicación...";
            if (!await _geoService.PedirPermisoAsync())
            {
                Estado = "Permiso de ubicación denegado";
                Procesando = false;
                return;
            }
        }

        // La foto se saca antes de empezar a buscar el GPS, así el usuario no espera dos cosas a la vez
        string? foto = null;
        if (config.PideFoto && !await AvisoFoto.ConfirmarAsync(SessionManager.Instance.EmpleadoId ?? 0, config.RetencionDias))
        {
            if (config.FotoObligatoria)
            {
                Estado = "Para fichar con foto tenés que estar de acuerdo. Consultalo con tu empresa.";
                Procesando = false;
                return;
            }
        }
        else if (config.PideFoto)
        {
            Estado = "Sacate una foto...";
            try { foto = await FotoFichaje.TomarAsync(); }
            catch (Exception ex)
            {
                if (config.FotoObligatoria) { Estado = $"No se pudo usar la cámara: {ex.Message}"; Procesando = false; return; }
            }

            if (foto is null && config.FotoObligatoria)
            {
                Estado = "Este fichaje requiere una foto.";
                Procesando = false;
                return;
            }
        }

        // Ubicación y biometría corren en paralelo: el GPS se obtiene mientras el usuario se autentica.
        var ubicacionTask = config.PideUbicacion
            ? _geoService.ObtenerUbicacionAsync()
            : Task.FromResult(new UbicacionResultado(true, 0, 0, null, false, null, string.Empty));
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
            Direccion = ubicacion.Direccion,
            FotoBase64 = foto
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
        {
            if (config.PideUbicacion) MostrarUbicacion(ubicacion.Lat, ubicacion.Lon);
            await ActualizarProximoAsync();
        }
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
