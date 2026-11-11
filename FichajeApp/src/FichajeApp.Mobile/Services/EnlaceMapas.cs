namespace FichajeApp.Mobile.Services;

// "Ver en Google Maps": abre las coordenadas exactas del fichaje en Google Maps (la app si está instalada, si no el navegador)
public static class EnlaceMapas
{
    // false si no se pudo abrir (sin navegador, por ejemplo)
    public static async Task<bool> AbrirAsync(double latitud, double longitud)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(UrlDeMapas.Google(latitud, longitud)));
            return true;
        }
        catch
        {
            await Dialogos.Aviso("No se pudo abrir el mapa", "No encontramos una app para abrir Google Maps en este teléfono.");
            return false;
        }
    }
}
