namespace FichajeApp.Mobile.Services;

// Código puro (sin MAUI ni Android): lo compila también el proyecto de tests.

// Una posición que informó el teléfono. Momento = cuándo se midió (no cuándo llegó a la app).
public record LecturaGps(double Lat, double Lon, double? Precision, DateTimeOffset Momento, bool EsFalsa, string Origen);

// De dónde salen las posiciones. En Android escucha a la vez al GPS, a las antenas y al WiFi (ver Platforms/Android).
public interface IFuenteDeUbicacion
{
    event Action<LecturaGps>? Lectura;
    bool Activa { get; }

    // Empieza a escuchar. Lanza InvalidOperationException con un mensaje para la persona si el GPS está apagado o falta el permiso.
    void Iniciar();
    void Detener();

    // Lo último que el teléfono ya tenía guardado (sirve para arrancar con algo en vez de con nada)
    IReadOnlyList<LecturaGps> UltimasConocidas();
}

// Qué lectura sirve para fichar y cuál es mejor que otra.
public static class PoliticaDeUbicacion
{
    // Una lectura más vieja que esto ya no dice dónde está la persona ahora
    public static readonly TimeSpan EdadMaxima = TimeSpan.FromSeconds(20);

    // Cuánto se espera, como máximo, a que aparezca una lectura que sirva. Pasado esto el servidor dirá si la última alcanza.
    public static readonly TimeSpan Plazo = TimeSpan.FromSeconds(12);

    // Como último recurso se acepta algo de hasta 2 minutos antes que dejar a la persona sin poder fichar (el servidor verifica igual)
    public static readonly TimeSpan EdadMaximaDeEmergencia = TimeSpan.FromMinutes(2);

    // Sirve para fichar si es reciente y cumple la precisión que acepta el servidor
    public static bool Sirve(LecturaGps? lectura, double precisionRequerida, DateTimeOffset ahora) =>
        lectura is { Precision: { } precision }
        && precision <= precisionRequerida
        && ahora - lectura.Momento <= EdadMaxima;

    // Con dos lecturas, cuál conviene guardar: entre las recientes gana la más precisa; una reciente siempre le gana a una vieja.
    public static LecturaGps Mejor(LecturaGps? actual, LecturaGps nueva, DateTimeOffset ahora)
    {
        if (actual is null) return nueva;

        var actualVieja = ahora - actual.Momento > EdadMaxima;
        var nuevaVieja = ahora - nueva.Momento > EdadMaxima;
        if (actualVieja != nuevaVieja) return actualVieja ? nueva : actual;

        var precisionActual = actual.Precision ?? double.MaxValue;
        var precisionNueva = nueva.Precision ?? double.MaxValue;
        if (precisionNueva != precisionActual) return precisionNueva < precisionActual ? nueva : actual;

        return nueva.Momento >= actual.Momento ? nueva : actual;
    }
}

// Mantiene la mejor posición reciente mientras la fuente está escuchando, así al fichar casi nunca hay que esperar.
// Mientras la pantalla de fichar está abierta se deja escuchando; al fichar, si ya hay una lectura que sirve, se usa al instante.
public class BuscadorDeUbicacion
{
    private readonly IFuenteDeUbicacion _fuente;
    private readonly TimeProvider _tiempo;
    private readonly object _candado = new();
    private LecturaGps? _mejor;

    // Texto de avance para mostrar en pantalla mientras se espera ("Buscando señal GPS… ±45 m")
    public Action<string>? Progreso { get; set; }

    public BuscadorDeUbicacion(IFuenteDeUbicacion fuente, TimeProvider? tiempo = null)
    {
        _fuente = fuente;
        _tiempo = tiempo ?? TimeProvider.System;
        _fuente.Lectura += Registrar;
    }

    public bool Activo => _fuente.Activa;

    public LecturaGps? Actual
    {
        get { lock (_candado) return _mejor; }
    }

    public void Iniciar()
    {
        if (_fuente.Activa) return;
        foreach (var conocida in _fuente.UltimasConocidas()) Registrar(conocida);
        _fuente.Iniciar();
    }

    public void Detener() => _fuente.Detener();

    // Espera hasta que haya una lectura que sirva (o se cumpla el plazo) y la devuelve. Si no hubo ninguna que sirva devuelve
    // la mejor que haya, siempre que no sea muy vieja; null si no hay nada. Si nadie lo había iniciado, escucha solo durante la espera.
    public async Task<LecturaGps?> EsperarAsync(double precisionRequerida, TimeSpan? plazo = null, CancellationToken cancelar = default)
    {
        var loInicieYo = !_fuente.Activa;
        if (loInicieYo) Iniciar();

        try
        {
            var limite = _tiempo.GetUtcNow() + (plazo ?? PoliticaDeUbicacion.Plazo);
            while (true)
            {
                var ahora = _tiempo.GetUtcNow();
                var actual = Actual;

                if (PoliticaDeUbicacion.Sirve(actual, precisionRequerida, ahora)) return actual;
                if (ahora >= limite || cancelar.IsCancellationRequested)
                    return actual is not null && ahora - actual.Momento <= PoliticaDeUbicacion.EdadMaximaDeEmergencia ? actual : null;

                Avisar(actual);
                try { await Task.Delay(100, cancelar); }
                catch (OperationCanceledException) { /* se devuelve lo que haya en la próxima vuelta */ }
            }
        }
        finally
        {
            if (loInicieYo) Detener();
        }
    }

    private void Registrar(LecturaGps lectura)
    {
        lock (_candado) _mejor = PoliticaDeUbicacion.Mejor(_mejor, lectura, _tiempo.GetUtcNow());
    }

    private void Avisar(LecturaGps? actual)
    {
        if (Progreso is null) return;
        Progreso(actual?.Precision is { } p ? $"Buscando señal GPS… ±{p:0} m" : "Buscando señal GPS…");
    }
}
