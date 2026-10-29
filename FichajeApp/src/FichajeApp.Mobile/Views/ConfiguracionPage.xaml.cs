using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class ConfiguracionPage : ContentPage
{
    private readonly ConfiguracionViewModel _viewModel;

    public ConfiguracionPage(ConfiguracionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.Guardada += async () => await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(".."));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.CargarEstadoAsync();
    }

    private async void OnCopiarCorreo(object sender, EventArgs e) => await _viewModel.CopiarCorreoAsync();
    private async void OnElegirArchivo(object sender, EventArgs e) => await _viewModel.ElegirArchivoAsync();
    private async void OnGuardar(object sender, EventArgs e) => await _viewModel.GuardarAsync();
}
