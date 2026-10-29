using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Teléfono compartido de una sucursal: el empleado toca su nombre, escribe su PIN y (si el cliente lo pide) se saca una foto.
// El PIN se escribe en esta misma pantalla, enmascarado, y no en un diálogo del sistema.
public class KioscoViewModel : VmBase
{
    private readonly ApiService _api;
    private readonly AuthService _auth;
    private List<KioscoEmpleado> _todos = new();
    private string _busqueda = string.Empty;
    private KioscoEmpleado? _elegido;
    private string _pin = string.Empty;

    public KioscoViewModel(ApiService api, AuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public ObservableCollection<KioscoEmpleado> Empleados { get; } = new();

    // Nombre de la sucursal = nombre de la cuenta con la que se abrió el teléfono
    public string Sucursal => SessionManager.Instance.NombreCompleto ?? "Sucursal";

    public string Busqueda
    {
        get => _busqueda;
        set { if (Set(ref _busqueda, value)) Filtrar(); }
    }

    public KioscoEmpleado? Elegido
    {
        get => _elegido;
        private set { if (Set(ref _elegido, value)) { Notificar(nameof(HayElegido)); Notificar(nameof(TituloPin)); } }
    }

    public bool HayElegido => Elegido is not null;
    public string TituloPin => Elegido is null ? string.Empty : $"{Elegido.Nombre}: {Elegido.Accion}";

    public string Pin { get => _pin; set => Set(ref _pin, value); }

    public Task CargarAsync() => ConEspera(async () =>
    {
        var r = await _api.ObtenerKioscoEmpleadosAsync();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        _todos = r.Datos;
        Filtrar();
        Notificar(nameof(Sucursal));
    });

    private void Filtrar()
    {
        var texto = Busqueda.Trim();
        Empleados.Clear();
        foreach (var e in _todos.Where(e => texto.Length == 0 || e.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase)))
            Empleados.Add(e);
    }

    public void Elegir(KioscoEmpleado empleado)
    {
        Elegido = empleado;
        Pin = string.Empty;
        Estado = string.Empty;
    }

    public void Cancelar()
    {
        Elegido = null;
        Pin = string.Empty;
    }

    public Task ConfirmarAsync() => ConEspera(async () =>
    {
        if (Elegido is null) return;
        if (Pin.Length != 4 || !Pin.All(char.IsDigit)) { Estado = "El PIN tiene 4 dígitos."; return; }

        // Si el cliente pide foto, se saca antes de mandar: es la prueba de quién fichó
        var config = await _api.ObtenerConfiguracionFichajeAsync("kiosco/configuracion");
        string? foto = null;
        if (config.PideFoto)
        {
            Estado = "Sacate una foto...";
            try { foto = await FotoFichaje.TomarAsync(); }
            catch (Exception ex)
            {
                if (config.FotoObligatoria) { Estado = $"No se pudo usar la cámara: {ex.Message}"; return; }
            }

            if (foto is null && config.FotoObligatoria) { Estado = "Este fichaje requiere una foto."; return; }
        }

        Estado = "Registrando...";
        var r = await _api.RegistrarFichajeAsync(new { empleadoId = Elegido.Id, pin = Pin, fotoBase64 = foto }, "kiosco/fichaje");
        Pin = string.Empty;
        Estado = r.Mensaje;
        if (!r.Exito) return; // se queda en el mismo empleado para reintentar o cancelar

        Elegido = null;
        var lista = await _api.ObtenerKioscoEmpleadosAsync(); // actualiza quién queda en "Salida"
        if (lista.Exito && lista.Datos is not null)
        {
            _todos = lista.Datos;
            Filtrar();
        }
    });

    // Cerrar la sesión del teléfono pide confirmación: lo usa quien lo administra, no los empleados
    public async Task CerrarSesionAsync()
    {
        if (await Dialogos.Confirmar("Cerrar sesión del teléfono",
                "Nadie va a poder fichar en este teléfono hasta que se vuelva a iniciar sesión con la cuenta de la sucursal.", "Cerrar sesión"))
            _auth.Logout();
    }
}
