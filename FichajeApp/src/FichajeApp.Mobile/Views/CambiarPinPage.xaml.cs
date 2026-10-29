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

    private async void OnGuardar(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(ActualEntry.Text) || string.IsNullOrEmpty(NuevoEntry.Text))
        {
            EstadoLabel.Text = "Completá el PIN actual y el nuevo.";
            return;
        }
        if (NuevoEntry.Text != RepetirEntry.Text)
        {
            EstadoLabel.Text = "El PIN nuevo y su repetición no coinciden.";
            return;
        }

        GuardarButton.IsEnabled = false;
        Espera.IsVisible = Espera.IsRunning = true;
        var r = await _api.CambiarMiPin(ActualEntry.Text, NuevoEntry.Text);
        Espera.IsVisible = Espera.IsRunning = false;
        GuardarButton.IsEnabled = true;

        if (!r.Exito)
        {
            EstadoLabel.Text = r.Mensaje;
            return;
        }

        await Dialogos.Aviso("Listo", "Tu PIN se actualizó.");
        await Shell.Current.GoToAsync("..");
    }
}
