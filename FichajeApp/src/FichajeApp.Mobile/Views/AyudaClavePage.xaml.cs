namespace FichajeApp.Mobile.Views;

// Ayuda del código de activación (solo texto e ilustraciones: no necesita ViewModel).
// Primero se explica para el cliente; los pasos del servidor quedan en un panel aparte.
public partial class AyudaClavePage : ContentPage
{
    public AyudaClavePage() => InitializeComponent();

    private void OnVerComoServidor(object sender, EventArgs e) => ServidorPanel.IsVisible = !ServidorPanel.IsVisible;

    private async void OnVolver(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
