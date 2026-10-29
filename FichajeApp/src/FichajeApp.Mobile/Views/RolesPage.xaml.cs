using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class RolesPage : ContentPage
{
    private readonly RolesViewModel _viewModel;

    public RolesPage(RolesViewModel viewModel)
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

    private async void OnNuevo(object sender, EventArgs e) => await _viewModel.NuevoAsync();

    private async void OnSeleccion(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not FilaRol rol) return;
        ((CollectionView)sender).SelectedItem = null;
        await _viewModel.AccionesAsync(rol);
    }
}
