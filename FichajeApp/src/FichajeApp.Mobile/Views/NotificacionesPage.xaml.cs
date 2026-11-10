using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Centro de notificaciones: avisos de la cuenta (bienvenida, cambios de PIN, cambios de horario).
// Al abrirlo se marcan como leídas; las que eran nuevas se ven en negrita durante esta visita.
public partial class NotificacionesPage : ContentPage
{
    private readonly AdminApiService _api;

    public NotificacionesPage(AdminApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();
    }

    private async void OnRefrescar(object? sender, EventArgs e)
    {
        await CargarAsync();
        Refresco.IsRefreshing = false;
    }

    private async Task CargarAsync()
    {
        var r = await _api.Notificaciones();
        if (!r.Exito || r.Datos is null)
        {
            Aviso.Texto = r.Mensaje;
            Aviso.IsVisible = true;
            return;
        }

        Aviso.IsVisible = false;
        Lista.ItemsSource = r.Datos.Items;

        if (r.Datos.NoLeidas > 0)
        {
            await _api.LeerNotificaciones();
            if (Shell.Current is AppShell shell) await shell.ActualizarGloboAsync();
        }
    }
}
