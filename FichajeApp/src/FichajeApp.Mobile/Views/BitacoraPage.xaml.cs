using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class BitacoraPage : ContentPage
{
    private readonly BitacoraViewModel _viewModel;

    public BitacoraPage(BitacoraViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.BuscarAsync();
    }

    private async void OnBuscar(object sender, EventArgs e) => await _viewModel.BuscarAsync();
    private async void OnMas(object sender, EventArgs e) => await _viewModel.MasAsync();
}
