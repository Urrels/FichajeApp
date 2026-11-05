using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class SitiosPage : ContentPage
{
    private readonly SitiosViewModel _viewModel;

    public SitiosPage(SitiosViewModel viewModel)
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

    private async void OnAgregar(object sender, EventArgs e) => await _viewModel.AgregarAsync();

    private async void OnSeleccion(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SitioAdminItem sitio) return;
        ((CollectionView)sender).SelectedItem = null; // permite volver a tocar el mismo
        await _viewModel.AccionesAsync(sitio);
    }
}
