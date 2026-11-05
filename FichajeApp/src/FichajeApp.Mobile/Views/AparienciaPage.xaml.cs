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

    private void OnEditarPrincipal(object sender, TappedEventArgs e) => _viewModel.Editando = 0;
    private void OnEditarTexto(object sender, TappedEventArgs e) => _viewModel.Editando = 1;
    private void OnEditarAcento(object sender, TappedEventArgs e) => _viewModel.Editando = 2;
    private void OnTextoAutomatico(object sender, EventArgs e) => _viewModel.VolverAlTextoAutomatico();
    private async void OnElegirLogo(object sender, EventArgs e) => await _viewModel.ElegirLogoAsync();
    private void OnQuitarLogo(object sender, EventArgs e) => _viewModel.QuitarLogo();
    private async void OnGuardar(object sender, EventArgs e) => await _viewModel.GuardarAsync();
    private async void OnRestaurar(object sender, EventArgs e) => await _viewModel.RestaurarAsync();
}
