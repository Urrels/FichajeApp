namespace FichajeApp.Api.Helpers;

public static class GeoValidator
{
    // Coordenadas del lugar de trabajo - reemplazar por las reales
    private const double LatPermitida = -34.603722;
    private const double LonPermitida = -58.381592;
    private const double RadioMetrosPermitido = 100;

    public static bool EstaEnRango(double lat, double lon)
    {
        var distancia = DistanciaMetros(lat, lon, LatPermitida, LonPermitida);
        return distancia <= RadioMetrosPermitido;
    }

    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // radio de la Tierra en metros
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double deg) => deg * (Math.PI / 180);
}
