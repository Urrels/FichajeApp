using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajesAdminPage : ContentPage
{
    private readonly FichajesAdminViewModel _viewModel;

    public FichajesAdminPage(FichajesAdminViewModel viewModel)
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
    private async void OnManual(object sender, EventArgs e) => await _viewModel.CargarManualAsync();

    private async void OnSeleccion(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FichajeIncompleto fichaje) return;
        ((CollectionView)sender).SelectedItem = null;
        await _viewModel.CorregirAsync(fichaje);
    }
}
