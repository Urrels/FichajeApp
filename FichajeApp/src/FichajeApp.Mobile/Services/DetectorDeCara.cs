using System.Text;
using Android.Gms.Tasks;
using Android.Graphics;
using Android.Runtime;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Face;

namespace FichajeApp.Mobile.Services;

// Busca caras en una foto con ML Kit (en el teléfono, sin internet: la foto no sale del teléfono para esta revisión).
public static class DetectorDeCara
{
    // Qué pasó en la última revisión, para poder ver desde el teléfono por qué se rechazó una foto
    // (por ejemplo "giros 0°:0 90°:1" = no vio caras derecha pero sí con la foto girada 90°). Vacío si todo anduvo.
    public static string Diagnostico { get; private set; } = "";

    // null si el detector no pudo correr (el llamador decide qué hacer); lista vacía = corrió y no hay ninguna cara
    public static async Task<IReadOnlyList<CaraDetectada>?> DetectarAsync(string fotoBase64)
    {
        Bitmap? bitmap = null;
        Diagnostico = "";
        try
        {
            var bytes = Convert.FromBase64String(fotoBase64);
            bitmap = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
            if (bitmap is null || bitmap.Width == 0) { Diagnostico = "no se pudo leer la foto"; return null; }

            var opciones = new FaceDetectorOptions.Builder()
                .SetPerformanceMode(FaceDetectorOptions.PerformanceModeAccurate)
                .SetLandmarkMode(FaceDetectorOptions.LandmarkModeAll)
                .SetClassificationMode(FaceDetectorOptions.ClassificationModeNone)
                .Build();
            using var detector = FaceDetection.GetClient(opciones);

            // El detector solo ve caras derechas: si la cámara guardó la foto girada no encuentra nada. Se prueba derecha y
            // girada a los lados (y al revés) y se queda con el primer giro donde aparece una cara.
            var resumen = new StringBuilder("giros");
            foreach (var giro in new[] { 0, 90, 270, 180 })
            {
                using var imagen = InputImage.FromBitmap(bitmap, giro);
                var rostros = await ProcesarAsync(detector, imagen);
                if (rostros is null) { Diagnostico = $"el detector falló en {giro}°: {UltimoError}"; return null; }

                resumen.Append($" {giro}°:{rostros.Count}");
                if (rostros.Count == 0) continue;

                // Con la foto girada 90° o 270° el ancho y el alto se intercambian
                bool de_lado = giro is 90 or 270;
                float ancho = de_lado ? bitmap.Height : bitmap.Width, alto = de_lado ? bitmap.Width : bitmap.Height;

                var caras = new List<CaraDetectada>();
                foreach (var rostro in rostros)
                {
                    var r = rostro.BoundingBox;
                    // Una cara de verdad tiene los dos ojos y la nariz ubicables; una mancha o un dibujo suelen no tenerlos
                    bool rasgos = rostro.GetLandmark(FaceLandmark.LeftEye) is not null
                        && rostro.GetLandmark(FaceLandmark.RightEye) is not null
                        && rostro.GetLandmark(FaceLandmark.NoseBase) is not null;
                    caras.Add(new CaraDetectada(r.Width() / ancho, r.Height() / alto, rasgos));
                }
                Diagnostico = resumen.ToString();
                return caras;
            }

            Diagnostico = resumen.ToString();
            return new List<CaraDetectada>();
        }
        catch (Exception ex)
        {
            Diagnostico = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
        finally { bitmap?.Recycle(); }
    }

    private static string UltimoError = "";

    // La lista de caras de ML Kit, o null si falló. Los resultados de Java se convierten con JavaCast: un "as" o un "is" no
    // reconoce el tipo en la versión Release de la app y daba siempre "sin caras".
    private static async Task<List<Face>?> ProcesarAsync(IFaceDetector detector, InputImage imagen)
    {
        var espera = new TaskCompletionSource<List<Face>?>();
        detector.Process(imagen)
            .AddOnSuccessListener(new Escucha(espera))
            .AddOnFailureListener(new Escucha(espera));
        return await espera.Task;
    }

    private sealed class Escucha : Java.Lang.Object, IOnSuccessListener, IOnFailureListener
    {
        private readonly TaskCompletionSource<List<Face>?> _espera;
        public Escucha(TaskCompletionSource<List<Face>?> espera) => _espera = espera;

        public void OnSuccess(Java.Lang.Object? resultado)
        {
            try
            {
                var rostros = new List<Face>();
                if (resultado is not null)
                {
                    var lista = resultado.JavaCast<Java.Util.IList>();
                    for (int i = 0; i < lista.Size(); i++)
                        if (lista.Get(i)?.JavaCast<Face>() is { } rostro) rostros.Add(rostro);
                }
                _espera.TrySetResult(rostros);
            }
            catch (Exception ex)
            {
                UltimoError = $"{ex.GetType().Name}: {ex.Message}";
                _espera.TrySetResult(null);
            }
        }

        public void OnFailure(Java.Lang.Exception e)
        {
            UltimoError = e.Message ?? e.GetType().Name;
            _espera.TrySetResult(null);
        }
    }
}
