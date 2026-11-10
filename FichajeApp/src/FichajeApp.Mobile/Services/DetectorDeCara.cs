using Android.Gms.Tasks;
using Android.Graphics;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Face;

namespace FichajeApp.Mobile.Services;

// Busca caras en una foto con ML Kit (en el teléfono, sin internet: la foto no sale del teléfono para esta revisión).
public static class DetectorDeCara
{
    // null si el detector no pudo correr (el llamador decide: no se bloquea a nadie por una falla del teléfono)
    public static async Task<IReadOnlyList<CaraDetectada>?> DetectarAsync(string fotoBase64)
    {
        Bitmap? bitmap = null;
        try
        {
            var bytes = Convert.FromBase64String(fotoBase64);
            bitmap = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
            if (bitmap is null || bitmap.Width == 0) return null;

            var opciones = new FaceDetectorOptions.Builder()
                .SetPerformanceMode(FaceDetectorOptions.PerformanceModeAccurate)
                .SetLandmarkMode(FaceDetectorOptions.LandmarkModeNone)
                .SetClassificationMode(FaceDetectorOptions.ClassificationModeNone)
                .Build();
            using var detector = FaceDetection.GetClient(opciones);
            using var imagen = InputImage.FromBitmap(bitmap, 0);

            var espera = new TaskCompletionSource<Java.Util.IList?>();
            detector.Process(imagen)
                .AddOnSuccessListener(new Escucha(espera))
                .AddOnFailureListener(new Escucha(espera));
            var lista = await espera.Task;
            if (lista is null) return null;

            var caras = new List<CaraDetectada>();
            for (int i = 0; i < lista.Size(); i++)
            {
                if (lista.Get(i) is Face cara)
                {
                    var r = cara.BoundingBox;
                    caras.Add(new CaraDetectada((float)r.Width() / bitmap.Width, (float)r.Height() / bitmap.Height));
                }
            }
            return caras;
        }
        catch { return null; }
        finally { bitmap?.Recycle(); }
    }

    private sealed class Escucha : Java.Lang.Object, IOnSuccessListener, IOnFailureListener
    {
        private readonly TaskCompletionSource<Java.Util.IList?> _espera;
        public Escucha(TaskCompletionSource<Java.Util.IList?> espera) => _espera = espera;
        public void OnSuccess(Java.Lang.Object? resultado) => _espera.TrySetResult(resultado as Java.Util.IList);
        public void OnFailure(Java.Lang.Exception e) => _espera.TrySetResult(null);
    }
}
