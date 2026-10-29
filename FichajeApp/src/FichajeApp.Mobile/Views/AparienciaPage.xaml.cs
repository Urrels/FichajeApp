using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class AparienciaPage : ContentPage
{
    private readonly AparienciaViewModel _viewModel;

    public AparienciaPage(AparienciaViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Cargar();
    }

    // Si se sale sin guardar, la app vuelve a lo que estaba guardado
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.DescartarVistaPrevia();
    }

    private async void OnPaletaPrincipal(object sender, EventArgs e) => await _viewModel.ElegirColorAsync("principal");
    private async void OnPaletaTexto(object sender, EventArgs e) => await _viewModel.ElegirColorAsync("del texto");
    private async void OnPaletaAcento(object sender, EventArgs e) => await _viewModel.ElegirColorAsync("de acento");
    private async void OnElegirLogo(object sender, EventArgs e) => await _viewModel.ElegirLogoAsync();
    private void OnQuitarLogo(object sender, EventArgs e) => _viewModel.QuitarLogo();
    private void OnVistaPrevia(object sender, EventArgs e) => _viewModel.VistaPrevia();
    private async void OnGuardar(object sender, EventArgs e) => await _viewModel.GuardarAsync();
    private async void OnRestaurar(object sender, EventArgs e) => await _viewModel.RestaurarAsync();
}
