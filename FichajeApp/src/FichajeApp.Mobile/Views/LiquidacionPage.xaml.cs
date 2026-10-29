using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class LiquidacionPage : ContentPage
{
    private readonly LiquidacionViewModel _viewModel;

    public LiquidacionPage(LiquidacionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnCalcular(object sender, EventArgs e) => await _viewModel.CalcularAsync();
    private async void OnCerrar(object sender, EventArgs e) => await _viewModel.CerrarAsync();
    private async void OnCerradas(object sender, EventArgs e) => await _viewModel.VerLiquidacionesAsync();
    private async void OnConfiguracion(object sender, EventArgs e) => await _viewModel.ConfiguracionAsync();

    private async void OnSeleccion(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ResumenEmpleadoItem empleado) return;
        ((CollectionView)sender).SelectedItem = null;
        await _viewModel.VerDiasAsync(empleado);
    }
}
