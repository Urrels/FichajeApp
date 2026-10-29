namespace FichajeApp.Mobile.Views;

// Ayuda para conseguir la clave de instalación (solo texto e ilustraciones: no necesita ViewModel)
public partial class AyudaClavePage : ContentPage
{
    public AyudaClavePage() => InitializeComponent();

    private async void OnVolver(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
