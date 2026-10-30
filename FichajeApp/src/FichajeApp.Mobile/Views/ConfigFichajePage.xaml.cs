using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class ConfigFichajePage : ContentPage
{
    private readonly ConfigFichajeViewModel _viewModel;

    public ConfigFichajePage(ConfigFichajeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CargarAsync();
    }

    private async void OnUbicacion(object sender, EventArgs e) => await _viewModel.ElegirUbicacionAsync();
    private async void OnModo(object sender, EventArgs e) => await _viewModel.ElegirModoAsync();
    private async void OnFoto(object sender, EventArgs e) => await _viewModel.ElegirFotoAsync();
    private async void OnGuardar(object sender, EventArgs e) => await _viewModel.GuardarAsync();
}
