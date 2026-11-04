using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Pantalla de supervisor: fichajes de hoy, quién está adentro e historial por fecha/empleado.
// Como FichajeViewModel, observa la sesión para volver al login si el token vence.
public class SupervisorViewModel : INotifyPropertyChanged, ISessionObserver
{
    private const string Todos = "Todos";

    private readonly ApiService _apiService;
    private bool _empleadosCargados;

    private string _modo = "Hoy";
    private string _estado = string.Empty;
    private bool _procesando;
    private string _empleadoSeleccionado = Todos;
    private DateTime _desde = DateTime.Today;
    private DateTime _hasta = DateTime.Today;
    private bool _unirRecorrido;

    public event Action? DatosActualizados;
    public event Action? SesionFinalizada;

    // La última consulta se guarda para poder repetirla sola (actualización automática) o al deslizar la lista
    private Func<Task<ResultadoConsulta<List<FichajeItem>>>>? _ultimaConsulta;
    private string _textoVacio = string.Empty;
    private string _firma = string.Empty;
    private bool _refrescando;

    public SupervisorViewModel(ApiService apiService)
    {
        _apiService = apiService;
        RefrescarCommand = new Command(async () => await RefrescarAsync(manual: true));
        SessionManager.Instance.Suscribir(this);
    }

    public ICommand RefrescarCommand { get; }

    public bool Refrescando
    {
        get => _refrescando;
        private set { _refrescando = value; OnPropertyChanged(); }
    }

    // Verdadero cuando la lista cambió por una acción de la persona (hay que encuadrar el mapa);
    // falso en una actualización automática (no se mueve el mapa mientras alguien lo está mirando)
    public bool EncuadrarMapa { get; private set; } = true;

    public bool EsHoy => Modo == "Hoy";
    public bool EsPresentes => Modo == "Presentes";

    public ObservableCollection<FichajeItem> Items { get; } = new();
    public ObservableCollection<string> Empleados { get; } = new() { Todos };

    public string Modo
    {
        get => _modo;
        private set { _modo = value; OnPropertyChanged(); OnPropertyChanged(nameof(EsHistorial)); OnPropertyChanged(nameof(EsHoy)); OnPropertyChanged(nameof(EsPresentes)); }
    }

    public bool EsHistorial => Modo == "Historial";

    public string Estado
    {
        get => _estado;
        private set { _estado = value; OnPropertyChanged(); }
    }

    public bool Procesando
    {
        get => _procesando;
        private set { _procesando = value; OnPropertyChanged(); }
    }

    public string EmpleadoSeleccionado
    {
        get => _empleadoSeleccionado;
        set { _empleadoSeleccionado = value ?? Todos; OnPropertyChanged(); }
    }

    public DateTime Desde
    {
        get => _desde;
        set { _desde = value; OnPropertyChanged(); }
    }

    public DateTime Hasta
    {
        get => _hasta;
        set { _hasta = value; OnPropertyChanged(); }
    }

    public bool UnirRecorrido
    {
        get => _unirRecorrido;
        set { _unirRecorrido = value; OnPropertyChanged(); }
    }

    // El recorrido solo tiene sentido con los fichajes de una sola persona
    public bool HayRecorrido =>
        UnirRecorrido && Items.Count > 1 && Items.Select(i => i.NombreCompleto).Distinct().Count() == 1;

    public async Task IniciarAsync()
    {
        await CargarEmpleadosAsync();
        await MostrarHoyAsync();
    }

    public async Task MostrarHoyAsync()
    {
        Modo = "Hoy";
        await CargarAsync(() => _apiService.ObtenerFichajesAsync(DateTime.Today, DateTime.Today, null),
            "Todavía no hay fichajes hoy");
    }

    public async Task MostrarPresentesAsync()
    {
        Modo = "Presentes";
        await CargarAsync(() => _apiService.ObtenerPresentesAsync(), "No hay nadie adentro ahora");
    }

    public async Task BuscarHistorialAsync()
    {
        Modo = "Historial";
        var empleado = EmpleadoSeleccionado == Todos ? null : EmpleadoSeleccionado;
        await CargarAsync(() => _apiService.ObtenerFichajesAsync(Desde, Hasta, empleado),
            "No hay fichajes en ese rango");
    }

    private async Task CargarEmpleadosAsync()
    {
        if (_empleadosCargados) return;

        var resultado = await _apiService.ObtenerEmpleadosAsync();
        if (!resultado.Exito || resultado.Datos is null) return;

        foreach (var nombre in resultado.Datos)
            Empleados.Add(nombre);
        _empleadosCargados = true;
    }

    private async Task CargarAsync(Func<Task<ResultadoConsulta<List<FichajeItem>>>> consulta, string textoVacio)
    {
        _ultimaConsulta = consulta;
        _textoVacio = textoVacio;
        Procesando = true;
        Estado = "Cargando...";

        var resultado = await consulta();

        Items.Clear();
        if (resultado.Exito && resultado.Datos is not null)
        {
            foreach (var item in resultado.Datos)
                Items.Add(item);
            _firma = Firma(resultado.Datos);
            Estado = TextoEstado();
        }
        else
        {
            _firma = string.Empty;
            Estado = resultado.Mensaje;
        }

        Procesando = false;
        EncuadrarMapa = true;
        OnPropertyChanged(nameof(HayRecorrido));
        DatosActualizados?.Invoke();
    }

    // Repite la última consulta sin tocar la pantalla si no hay nada nuevo. Sirve para que el mapa y la lista
    // se pongan al día solos mientras el supervisor los mira. Un error de una actualización automática no pisa lo que se ve.
    public async Task RefrescarAsync(bool manual = false)
    {
        if (Procesando || _ultimaConsulta is null)
        {
            Refrescando = false;
            return;
        }

        if (manual) Refrescando = true;
        try
        {
            var resultado = await _ultimaConsulta();
            if (resultado.Exito && resultado.Datos is not null)
            {
                var firma = Firma(resultado.Datos);
                if (firma != _firma)
                {
                    _firma = firma;
                    Items.Clear();
                    foreach (var item in resultado.Datos)
                        Items.Add(item);
                    EncuadrarMapa = false;
                    OnPropertyChanged(nameof(HayRecorrido));
                    DatosActualizados?.Invoke();
                }
                Estado = TextoEstado();
            }
            else if (manual)
            {
                Estado = resultado.Mensaje;
            }
        }
        finally { Refrescando = false; }
    }

    private static string Firma(IEnumerable<FichajeItem> items) =>
        string.Join("|", items.Select(i => $"{i.NombreCompleto}/{i.FechaHora:O}/{i.TipoFichaje}/{i.Latitud}/{i.Longitud}"));

    // "3 fichajes · 1 sin ubicación (no se ve en el mapa) · actualizado 15:42"
    private string TextoEstado()
    {
        if (Items.Count == 0) return _textoVacio;
        var sinUbicacion = Items.Count(i => !i.TieneUbicacion);
        return $"{Items.Count} fichaje(s)"
            + (sinUbicacion > 0 ? $" · {sinUbicacion} sin ubicación (no se ven en el mapa)" : "")
            + $" · actualizado {DateTime.Now:HH:mm}";
    }

    // --- ISessionObserver ---
    public void OnSesionIniciada(string nombreCompleto) { }

    public void OnSesionFinalizada() => SesionFinalizada?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
