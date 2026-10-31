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

    // Si la lista no se pudo cargar no hay nada que hacer en la pantalla: se muestra una pantalla de error con "Reintentar"
    private string? _errorDeCarga;
    public string? ErrorDeCarga { get => _errorDeCarga; private set { if (Set(ref _errorDeCarga, value)) { Notificar(nameof(HayError)); Notificar(nameof(SinError)); } } }
    public bool HayError => !string.IsNullOrWhiteSpace(ErrorDeCarga);
    public bool SinError => !HayError;

    public bool HayElegido => Elegido is not null;
    public string TituloPin => Elegido is null ? string.Empty : $"{Elegido.Nombre}: {Elegido.Accion}";

    public string Pin { get => _pin; set => Set(ref _pin, value); }

    public Task CargarAsync() => ConEspera(async () =>
    {
        var r = await _api.ObtenerKioscoEmpleadosAsync();
        if (!r.Exito || r.Datos is null) { ErrorDeCarga = r.Mensaje; return; }
        ErrorDeCarga = null;

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
        Reiniciar();
    }

    public void Cancelar()
    {
        Elegido = null;
        Pin = string.Empty;
    }

    public Task ConfirmarAsync() => ConEspera(async () =>
    {
        if (Elegido is null) return;
        if (Pin.Length != 4 || !Pin.All(char.IsDigit)) { Fallar("El PIN tiene 4 dígitos."); return; }

        // Si el cliente pide foto, se saca antes de mandar: es la prueba de quién fichó
        var config = await _api.ObtenerConfiguracionFichajeAsync("kiosco/configuracion");
        string? foto = null;
        if (config.PideFoto && !await AvisoFoto.ConfirmarAsync(Elegido.Id, config.RetencionDias))
        {
            if (config.FotoObligatoria)
            {
                Fallar("Para fichar con foto tenés que estar de acuerdo. Consultalo con tu empresa.");
                return;
            }
        }
        else if (config.PideFoto)
        {
            Avisar("Sacate una foto...");
            try { foto = await FotoFichaje.TomarAsync(); }
            catch (Exception ex)
            {
                if (config.FotoObligatoria) { Fallar(TextosDeError.CamaraNoDisponible); return; }
            }

            if (foto is null && config.FotoObligatoria) { Fallar("Este fichaje requiere una foto. Sacate una y volvé a intentar."); return; }
        }

        Avisar("Registrando...");
        var r = await _api.RegistrarFichajeAsync(new { empleadoId = Elegido.Id, pin = Pin, fotoBase64 = foto }, "kiosco/fichaje");
        Pin = string.Empty;
        if (!r.Exito) { Fallar(r.Mensaje); return; } // se queda en el mismo empleado para reintentar o cancelar

        var nombre = Elegido.Nombre;
        Avisar(string.Empty);
        Elegido = null;
        var lista = await _api.ObtenerKioscoEmpleadosAsync(); // actualiza quién queda en "Salida"
        if (lista.Exito && lista.Datos is not null)
        {
            _todos = lista.Datos;
            Filtrar();
        }
        _ = MostrarConfirmacionAsync($"{nombre}: {TextosDeError.FichajeRegistrado(r.Tipo ?? "Fichaje", DateTime.Now)}");
    });

    private bool _hayFalla;
    private bool _confirmacionVisible;
    private string _confirmacionTexto = string.Empty;

    // Un error del fichaje (PIN mal, foto) se ve como banner rojo; el avance ("Registrando…") como texto común
    public bool HayFalla => _hayFalla;
    public bool HayAvance => !_hayFalla && !_confirmacionVisible && !string.IsNullOrWhiteSpace(Estado);
    public bool ConfirmacionVisible => _confirmacionVisible;
    public string ConfirmacionTexto => _confirmacionTexto;

    private void Avisar(string texto)
    {
        Estado = texto;
        _hayFalla = false;
        Notificar(nameof(HayFalla));
        Notificar(nameof(HayAvance));
    }

    private void Fallar(string texto)
    {
        Estado = texto;
        _hayFalla = true;
        Notificar(nameof(HayFalla));
        Notificar(nameof(HayAvance));
    }

    private void Reiniciar()
    {
        _confirmacionVisible = false;
        Avisar(string.Empty);
        Notificar(nameof(ConfirmacionVisible));
    }

    // Quien ficha ve el resultado desde lejos: tarjeta grande con nombre, tipo y hora, y una vibración corta
    private async Task MostrarConfirmacionAsync(string texto)
    {
        _confirmacionTexto = texto;
        _confirmacionVisible = true;
        Notificar(nameof(ConfirmacionTexto));
        Notificar(nameof(ConfirmacionVisible));
        Notificar(nameof(HayAvance));
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { /* sin vibración en este teléfono */ }

        await Task.Delay(TimeSpan.FromSeconds(6));
        if (_confirmacionTexto != texto) return; // ya hay una confirmación más nueva
        _confirmacionVisible = false;
        Notificar(nameof(ConfirmacionVisible));
        Notificar(nameof(HayAvance));
    }

    // Cerrar la sesión del teléfono pide confirmación: lo usa quien lo administra, no los empleados
    public async Task CerrarSesionAsync()
    {
        if (await Dialogos.Confirmar("Cerrar sesión del teléfono",
                "Nadie va a poder fichar en este teléfono hasta que se vuelva a iniciar sesión con la cuenta de la sucursal.", "Cerrar sesión"))
            _auth.Logout();
    }
}
