using Microsoft.Maui.Graphics.Platform;
#if ANDROID
using Android.Graphics;
using Android.Media;
#endif

namespace FichajeApp.Mobile.Services;

// Achica una imagen elegida o sacada con la cámara.
//
// IMPORTANTE: una foto de cámara tiene 12 a 50 megapíxeles. Decodificarla entera ocupa de 50 a 200 MB de memoria y Android
// cierra la app (a la persona le parecía que "se cerraba al apretar la tilde"). En Android se lee ya reducida (inSampleSize)
// y recién después se ajusta el tamaño y la rotación.
public static class ReductorDeImagenes
{
    // Devuelve JPG, o PNG (conserva la transparencia) si se permite y entra en pesoMaximoPng
    public static async Task<byte[]> ReducirAsync(FileResult archivo, int lado, bool permitirPng, int calidadJpeg, int pesoMaximoPng)
    {
#if ANDROID
        var ruta = archivo.FullPath;
        string? temporal = null;
        try
        {
            // Si la ruta no es un archivo que se pueda abrir directamente, se copia a un temporal
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta))
            {
                temporal = System.IO.Path.Combine(FileSystem.CacheDirectory, $"reducir_{Guid.NewGuid():N}.tmp");
                await using (var origen = await archivo.OpenReadAsync())
                await using (var destino = File.Create(temporal))
                    await origen.CopyToAsync(destino);
                ruta = temporal;
            }

            return await Task.Run(() => ReducirAndroid(ruta, lado, permitirPng, calidadJpeg, pesoMaximoPng));
        }
        finally
        {
            if (temporal is not null) try { File.Delete(temporal); } catch { /* es de la caché: se limpia sola */ }
        }
#else
        await using var flujo = await archivo.OpenReadAsync();
        using Microsoft.Maui.Graphics.IImage imagen = PlatformImage.FromStream(flujo);
        using Microsoft.Maui.Graphics.IImage chica = imagen.Downsize(lado, true);

        if (permitirPng)
        {
            var png = chica.AsBytes(Microsoft.Maui.Graphics.ImageFormat.Png);
            if (png.Length <= pesoMaximoPng) return png;
        }
        return chica.AsBytes(Microsoft.Maui.Graphics.ImageFormat.Jpeg, calidadJpeg / 100f);
#endif
    }

#if ANDROID
    private static byte[] ReducirAndroid(string ruta, int lado, bool permitirPng, int calidadJpeg, int pesoMaximoPng)
    {
        // 1) Solo se leen las medidas, sin decodificar la imagen
        var medidas = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(ruta, medidas);
        if (medidas.OutWidth <= 0 || medidas.OutHeight <= 0)
            throw new InvalidOperationException("No se pudo leer la imagen.");

        // 2) Se decodifica reducida a la mitad, cuarta parte... mientras siga siendo más grande que lo necesario
        int muestra = 1;
        while (Math.Max(medidas.OutWidth, medidas.OutHeight) / (muestra * 2) >= lado) muestra *= 2;

        using var leida = BitmapFactory.DecodeFile(ruta, new BitmapFactory.Options { InSampleSize = muestra })
                          ?? throw new InvalidOperationException("No se pudo leer la imagen.");

        // 3) Ajuste fino de tamaño y giro según el EXIF (las fotos de la cámara suelen venir "acostadas")
        using var matriz = new Matrix();
        float escala = Math.Min(1f, lado / (float)Math.Max(leida.Width, leida.Height));
        matriz.PostScale(escala, escala);
        int giro = Giro(ruta);
        if (giro != 0) matriz.PostRotate(giro);

        using var final = Bitmap.CreateBitmap(leida, 0, 0, leida.Width, leida.Height, matriz, true)
                          ?? throw new InvalidOperationException("No se pudo achicar la imagen.");

        using var salida = new MemoryStream();
        if (permitirPng)
        {
            final.Compress(Bitmap.CompressFormat.Png!, 100, salida);
            if (salida.Length <= pesoMaximoPng) return salida.ToArray();
            salida.SetLength(0);
        }
        final.Compress(Bitmap.CompressFormat.Jpeg!, calidadJpeg, salida);
        return salida.ToArray();
    }

    private static int Giro(string ruta)
    {
        try
        {
            using var exif = new ExifInterface(ruta);
            return exif.GetAttributeInt(ExifInterface.TagOrientation, 1) switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
        }
        catch { return 0; }
    }
#endif
}
