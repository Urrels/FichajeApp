using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class UsuariosPage : ContentPage
{
    private readonly UsuariosViewModel _viewModel;

    public UsuariosPage(UsuariosViewModel viewModel)
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

    private async void OnBuscar(object sender, EventArgs e) => await _viewModel.CargarAsync();
    private async void OnNuevo(object sender, EventArgs e) => await _viewModel.NuevoAsync();

    private async void OnSeleccion(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not UsuarioAdmin usuario) return;
        ((CollectionView)sender).SelectedItem = null; // permite volver a tocar el mismo
        await _viewModel.AccionesAsync(usuario);
    }
}
