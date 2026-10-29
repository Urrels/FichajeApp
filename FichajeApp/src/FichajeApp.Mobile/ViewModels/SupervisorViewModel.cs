using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public SupervisorViewModel(ApiService apiService)
    {
        _apiService = apiService;
        SessionManager.Instance.Suscribir(this);
    }

    public ObservableCollection<FichajeItem> Items { get; } = new();
    public ObservableCollection<string> Empleados { get; } = new() { Todos };

    public string Modo
    {
        get => _modo;
        private set { _modo = value; OnPropertyChanged(); OnPropertyChanged(nameof(EsHistorial)); }
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
        Procesando = true;
        Estado = "Cargando...";

        var resultado = await consulta();

        Items.Clear();
        if (resultado.Exito && resultado.Datos is not null)
        {
            foreach (var item in resultado.Datos)
                Items.Add(item);
            Estado = Items.Count == 0 ? textoVacio : $"{Items.Count} fichaje(s)";
        }
        else
        {
            Estado = resultado.Mensaje;
        }

        Procesando = false;
        OnPropertyChanged(nameof(HayRecorrido));
        DatosActualizados?.Invoke();
    }

    // --- ISessionObserver ---
    public void OnSesionIniciada(string nombreCompleto) { }

    public void OnSesionFinalizada() => SesionFinalizada?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
