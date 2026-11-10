namespace FichajeApp.Mobile.Services;

// Una cara encontrada en la foto, con su tamaño relativo al de la imagen (0 a 1)
public record CaraDetectada(float AnchoRelativo, float AltoRelativo);

public enum VeredictoCara { Valida, SinCara, VariasCaras, MuyLejos }

// Qué se le exige a una foto de perfil: una sola cara y que se vea de cerca. Sin dependencias del teléfono, así se prueba.
public static class ReglasDeCara
{
    // La cara tiene que ocupar al menos esta parte del ancho de la foto
    public const float AnchoMinimo = 0.25f;

    public static VeredictoCara Evaluar(IReadOnlyList<CaraDetectada> caras)
    {
        if (caras.Count == 0) return VeredictoCara.SinCara;
        if (caras.Count > 1) return VeredictoCara.VariasCaras;
        return caras[0].AnchoRelativo < AnchoMinimo ? VeredictoCara.MuyLejos : VeredictoCara.Valida;
    }

    public static string Mensaje(VeredictoCara v) => v switch
    {
        VeredictoCara.SinCara => "No se ve ninguna cara. Mirá de frente a la cámara, con buena luz y sin nada que te tape.",
        VeredictoCara.VariasCaras => "Se ven varias personas. La foto tiene que ser solo de tu cara.",
        VeredictoCara.MuyLejos => "Estás muy lejos. Acercá la cara a la cámara para que se te vea bien.",
        _ => string.Empty
    };
}
