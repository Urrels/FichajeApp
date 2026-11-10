using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.Views.Componentes;

namespace FichajeApp.Mobile.Views;

// "Olvidé mi PIN": el servidor manda un código al mail de la persona y con ese código elige un PIN nuevo.
public partial class OlvidePinPage : ContentPage
{
    private readonly AuthService _auth;

    public OlvidePinPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    private void Mostrar(string mensaje, bool error = true)
    {
        Aviso.Severidad = error ? Severidad.Error : Severidad.Exito;
        Aviso.Texto = mensaje;
        Aviso.IsVisible = true;
    }

    private void Ocupado(bool si)
    {
        Espera.IsVisible = Espera.IsRunning = si;
        EnviarButton.IsEnabled = CambiarButton.IsEnabled = !si;
    }

    private async void OnEnviarCodigo(object sender, EventArgs e) => await PedirCodigoAsync();
    private async void OnPedirOtro(object sender, EventArgs e) => await PedirCodigoAsync();

    private async Task PedirCodigoAsync()
    {
        var email = EmailEntry.Text?.Trim() ?? "";
        if (email.Length == 0) { Mostrar("Escribí tu email."); return; }

        Aviso.IsVisible = false;
        Ocupado(true);
        var (ok, mensaje) = await _auth.SolicitarCodigoAsync(email);
        Ocupado(false);

        if (!ok) { Mostrar(mensaje); return; }

        Paso1.IsVisible = false;
        Paso2.IsVisible = true;
        Indicacion.Text = $"{mensaje}\nEl código vale 15 minutos.";
    }

    private async void OnCambiar(object sender, EventArgs e)
    {
        var codigo = CodigoEntry.Text?.Trim() ?? "";
        var pin = PinEntry.Text ?? "";
        if (codigo.Length != 6 || pin.Length != 4) { Mostrar("Completá el código de 6 dígitos y un PIN de 4 dígitos."); return; }
        if (pin != RepetirEntry.Text) { Mostrar("El PIN nuevo y su repetición no coinciden."); return; }

        Aviso.IsVisible = false;
        Ocupado(true);
        var (ok, mensaje) = await _auth.RestablecerPinAsync(EmailEntry.Text!.Trim(), codigo, pin);
        Ocupado(false);

        if (!ok) { Mostrar(mensaje); return; }

        // El PIN que había recordado para entrar con huella ya no sirve
        CredencialesRecordadas.Borrar();
        await Dialogos.Aviso("Listo", mensaje);
        await Shell.Current.GoToAsync("..");
    }
}
