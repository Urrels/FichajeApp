using FichajeApp.Mobile.Services;
using FichajeApp.Mobile.ViewModels;

namespace FichajeApp.Mobile.Views;

public partial class FichajePage : ContentPage
{
    private readonly FichajeViewModel _viewModel;

    public FichajePage(ApiService apiService)
    {
        InitializeComponent();
        _viewModel = new FichajeViewModel(apiService);
        _viewModel.SesionFinalizada += OnSesionFinalizada;
        BindingContext = _viewModel;
    }

    private async void OnFicharClicked(object sender, EventArgs e)
    {
        await _viewModel.FicharAsync();
    }

    private async void OnAbrirMapsClicked(object sender, EventArgs e)
    {
        await _viewModel.AbrirEnMapsAsync();
    }

    private async void OnSesionFinalizada()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
