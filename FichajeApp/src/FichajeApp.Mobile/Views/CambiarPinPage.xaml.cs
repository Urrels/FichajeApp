using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// El PIN se escribe en campos ocultos (los diálogos de texto no pueden enmascararlo)
public partial class CambiarPinPage : ContentPage
{
    private readonly AdminApiService _api;

    public CambiarPinPage(AdminApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    private void Mostrar(string mensaje)
    {
        Aviso.Texto = mensaje;
        Aviso.IsVisible = true;
    }

    private async void OnGuardar(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(ActualEntry.Text) || string.IsNullOrEmpty(NuevoEntry.Text))
        {
            Mostrar("Completá el PIN actual y el nuevo.");
            return;
        }
        if (NuevoEntry.Text != RepetirEntry.Text)
        {
            Mostrar("El PIN nuevo y su repetición no coinciden.");
            return;
        }

        Aviso.IsVisible = false;
        GuardarButton.IsEnabled = false;
        Espera.IsVisible = Espera.IsRunning = true;
        var r = await _api.CambiarMiPin(ActualEntry.Text, NuevoEntry.Text);
        Espera.IsVisible = Espera.IsRunning = false;
        GuardarButton.IsEnabled = true;

        if (!r.Exito)
        {
            Mostrar(r.Mensaje);
            return;
        }

        await CredencialesRecordadas.ActualizarPinAsync(NuevoEntry.Text);   // la huella sigue entrando con el PIN nuevo
        await Dialogos.Aviso("Listo", "Tu PIN se actualizó.");
        await Shell.Current.GoToAsync("..");
    }
}
