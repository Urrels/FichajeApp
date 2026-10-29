using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

// Menú de administración: cada opción aparece solo si el usuario tiene el permiso correspondiente
public partial class AdminPage : ContentPage
{
    private readonly AuthService _authService;

    public AdminPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var sesion = SessionManager.Instance;
        SaludoLabel.Text = $"Hola, {sesion.NombreCompleto}";
        UsuariosButton.IsVisible = sesion.TienePermiso(NombresPermisos.AdministrarUsuarios);
        RolesButton.IsVisible = sesion.TienePermiso(NombresPermisos.GestionRoles);
        BitacoraButton.IsVisible = sesion.TienePermiso(NombresPermisos.VerBitacora);
        FichajesButton.IsVisible = sesion.TienePermiso(NombresPermisos.CorregirFichajes);
        LiquidacionButton.IsVisible = sesion.TienePermiso(NombresPermisos.LiquidarHoras);
        AparienciaButton.IsVisible = sesion.TienePermiso(NombresPermisos.GestionarApariencia);
        PinButton.IsVisible = sesion.TienePermiso(NombresPermisos.CambiarPin);
    }

    private async void OnUsuarios(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(UsuariosPage));
    private async void OnRoles(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(RolesPage));
    private async void OnBitacora(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(BitacoraPage));
    private async void OnFichajes(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(FichajesAdminPage));
    private async void OnLiquidacion(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(LiquidacionPage));
    private async void OnApariencia(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(AparienciaPage));
    private async void OnCambiarPin(object sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(CambiarPinPage));

    private void OnCerrarSesion(object sender, EventArgs e) => _authService.Logout();
}
