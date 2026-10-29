using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class KioscoPage : ContentPage
{
    private readonly KioscoViewModel _viewModel;

    public KioscoPage(KioscoViewModel viewModel)
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

    private void OnEmpleado(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not KioscoEmpleado empleado) return;

        ((CollectionView)sender).SelectedItem = null; // la lista queda lista para el próximo empleado
        _viewModel.Elegir(empleado);
    }

    private void OnCancelar(object sender, EventArgs e) => _viewModel.Cancelar();
    private async void OnConfirmar(object sender, EventArgs e) => await _viewModel.ConfirmarAsync();
    private async void OnCerrarSesion(object sender, EventArgs e) => await _viewModel.CerrarSesionAsync();
}
