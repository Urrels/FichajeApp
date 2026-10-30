namespace FichajeApp.Mobile.Services;

// Selfie del fichaje: se saca con la cámara, se achica y se manda como base64 (JPG de ~100 KB)
public static class FotoFichaje
{
    private const int LadoMaximo = 640;
    private const int Calidad = 70; // calidad JPG, de 0 a 100

    public static bool Disponible => MediaPicker.Default.IsCaptureSupported;

    // null si la persona canceló o no hay cámara; lanza si el permiso fue denegado
    public static async Task<string?> TomarAsync()
    {
        if (!Disponible) return null;

        var estado = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (estado != PermissionStatus.Granted)
            estado = await Permissions.RequestAsync<Permissions.Camera>();
        if (estado != PermissionStatus.Granted)
            throw new PermissionException("Permiso de cámara denegado");

        // Primero la cámara frontal dentro de la app; si el teléfono no tiene o no se puede abrir, la cámara del sistema
        try
        {
            var selfie = new Views.SelfiePage();
            await Shell.Current.Navigation.PushModalAsync(selfie, animated: true);
            return await selfie.Resultado;
        }
        catch (Views.SelfieNoDisponibleException) { /* se usa la cámara del sistema */ }
        catch (Exception ex) when (ex is not PermissionException) { /* idem: nunca dejar a la persona sin poder fichar */ }

        var foto = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Sacate una foto" });
        if (foto is null) return null;

        // Se achica mientras se lee: decodificar la foto entera de la cámara agota la memoria y cierra la app
        var bytes = await ReductorDeImagenes.ReducirAsync(foto, LadoMaximo, permitirPng: false, calidadJpeg: Calidad, pesoMaximoPng: 0);
        return Convert.ToBase64String(bytes);
    }
}
