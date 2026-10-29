using Microsoft.Maui.Graphics.Platform;

namespace FichajeApp.Mobile.Services;

// Selfie del fichaje: se saca con la cámara, se achica y se manda como base64 (JPG de ~100 KB)
public static class FotoFichaje
{
    private const float LadoMaximo = 640f;
    private const float Calidad = 0.7f;

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

        var foto = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Sacate una foto" });
        if (foto is null) return null;

        await using var origen = await foto.OpenReadAsync();
        using Microsoft.Maui.Graphics.IImage imagen = PlatformImage.FromStream(origen);
        using Microsoft.Maui.Graphics.IImage chica = imagen.Downsize(LadoMaximo, true);
        return Convert.ToBase64String(chica.AsBytes(Microsoft.Maui.Graphics.ImageFormat.Jpeg, Calidad));
    }
}
