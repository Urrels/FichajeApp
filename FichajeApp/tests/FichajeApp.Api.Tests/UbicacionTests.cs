using System.Diagnostics;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

// Una fuente de posiciones que se maneja a mano: se "inyectan" lecturas como si las mandara el teléfono
public class FuenteFalsa : IFuenteDeUbicacion
{
    public event Action<LecturaGps>? Lectura;
    public bool Activa { get; private set; }
    public int Inicios { get; private set; }
    public int Detenciones { get; private set; }
    public List<LecturaGps> Conocidas { get; } = new();
    public Exception? FalloAlIniciar { get; set; }

    public void Iniciar()
    {
        if (FalloAlIniciar is not null) throw FalloAlIniciar;
        Activa = true;
        Inicios++;
    }

    public void Detener() { Activa = false; Detenciones++; }
    public IReadOnlyList<LecturaGps> UltimasConocidas() => Conocidas;
    public void Llega(LecturaGps l) => Lectura?.Invoke(l);
}

public class PoliticaDeUbicacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static LecturaGps Lectura(double? precision, int segundosAtras = 0, bool falsa = false, string origen = "gps") =>
        new(-34.6, -58.4, precision, Ahora.AddSeconds(-segundosAtras), falsa, origen);

    [Theory]
    [InlineData(30, 0, 50, true)]     // cumple y es de ahora
    [InlineData(50, 0, 50, true)]     // justo en el límite
    [InlineData(51, 0, 50, false)]    // un metro de más
    [InlineData(10, 25, 50, false)]   // muy buena, pero vieja
    [InlineData(10, 19, 50, true)]    // todavía vale
    public void Una_lectura_sirve_si_es_reciente_y_cumple_la_precision(double precision, int segundos, double requerida, bool esperado) =>
        Assert.Equal(esperado, PoliticaDeUbicacion.Sirve(Lectura(precision, segundos), requerida, Ahora));

    [Fact]
    public void Sin_lectura_o_sin_precision_no_sirve()
    {
        Assert.False(PoliticaDeUbicacion.Sirve(null, 50, Ahora));
        Assert.False(PoliticaDeUbicacion.Sirve(Lectura(null), 50, Ahora));
    }

    [Fact]
    public void Entre_dos_lecturas_recientes_gana_la_mas_precisa()
    {
        var wifi = Lectura(45, 1, origen: "network");
        var gps = Lectura(8, 3, origen: "gps");

        Assert.Equal("gps", PoliticaDeUbicacion.Mejor(wifi, gps, Ahora).Origen);
        Assert.Equal("gps", PoliticaDeUbicacion.Mejor(gps, wifi, Ahora).Origen);
    }

    [Fact]
    public void Una_reciente_le_gana_a_una_vieja_aunque_sea_menos_precisa()
    {
        var vieja = Lectura(5, 60, origen: "gps");
        var reciente = Lectura(40, 2, origen: "network");

        Assert.Equal("network", PoliticaDeUbicacion.Mejor(vieja, reciente, Ahora).Origen);
        Assert.Equal("network", PoliticaDeUbicacion.Mejor(reciente, vieja, Ahora).Origen);
    }

    [Fact]
    public void Con_la_misma_precision_gana_la_mas_nueva_y_una_sin_precision_pierde()
    {
        var a = Lectura(20, 5, origen: "a");
        var b = Lectura(20, 1, origen: "b");
        Assert.Equal("b", PoliticaDeUbicacion.Mejor(a, b, Ahora).Origen);

        Assert.Equal("a", PoliticaDeUbicacion.Mejor(a, Lectura(null, 0, origen: "sin"), Ahora).Origen);
        Assert.Equal("nueva", PoliticaDeUbicacion.Mejor(null, Lectura(30, 0, origen: "nueva"), Ahora).Origen);
    }
}

public class BuscadorDeUbicacionTests
{
    private static LecturaGps Ahora(double? precision, string origen = "gps", bool falsa = false) =>
        new(-34.6, -58.4, precision, DateTimeOffset.UtcNow, falsa, origen);

    [Fact]
    public async Task Si_ya_hay_una_lectura_buena_se_devuelve_al_instante()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);
        buscador.Iniciar();
        fuente.Llega(Ahora(25));

        var reloj = Stopwatch.StartNew();
        var lectura = await buscador.EsperarAsync(50, TimeSpan.FromSeconds(10));

        Assert.Equal(25, lectura!.Precision);
        Assert.True(reloj.ElapsedMilliseconds < 300, $"Tardó {reloj.ElapsedMilliseconds} ms");
        Assert.True(fuente.Activa);   // quien lo había iniciado (la pantalla) lo sigue escuchando
    }

    [Fact]
    public async Task Espera_a_que_llegue_una_lectura_que_cumpla_y_la_toma_en_cuanto_aparece()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);
        buscador.Iniciar();
        fuente.Llega(Ahora(120, "network"));   // todavía no alcanza

        var espera = buscador.EsperarAsync(50, TimeSpan.FromSeconds(5));
        await Task.Delay(250);
        Assert.False(espera.IsCompleted);

        fuente.Llega(Ahora(35, "gps"));
        var lectura = await espera;

        Assert.Equal("gps", lectura!.Origen);
    }

    [Fact]
    public async Task Al_cumplirse_el_plazo_devuelve_la_mejor_aunque_no_cumpla_y_el_servidor_decide()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);
        buscador.Iniciar();
        fuente.Llega(Ahora(90, "network"));
        fuente.Llega(Ahora(70, "gps"));

        var lectura = await buscador.EsperarAsync(50, TimeSpan.FromMilliseconds(300));

        Assert.Equal(70, lectura!.Precision);
    }

    [Fact]
    public async Task Sin_ninguna_lectura_devuelve_nulo_y_una_demasiado_vieja_no_se_acepta()
    {
        var fuente = new FuenteFalsa();
        fuente.Conocidas.Add(new LecturaGps(-34.6, -58.4, 10, DateTimeOffset.UtcNow.AddMinutes(-10), false, "gps"));
        var buscador = new BuscadorDeUbicacion(fuente);

        Assert.Null(await buscador.EsperarAsync(50, TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task Arranca_con_lo_ultimo_que_el_telefono_ya_sabia()
    {
        var fuente = new FuenteFalsa();
        fuente.Conocidas.Add(new LecturaGps(-34.6, -58.4, 18, DateTimeOffset.UtcNow.AddSeconds(-5), false, "network"));
        var buscador = new BuscadorDeUbicacion(fuente);

        var reloj = Stopwatch.StartNew();
        var lectura = await buscador.EsperarAsync(50, TimeSpan.FromSeconds(5));

        Assert.Equal("network", lectura!.Origen);
        Assert.True(reloj.ElapsedMilliseconds < 300);
    }

    [Fact]
    public async Task Si_nadie_estaba_escuchando_escucha_solo_durante_la_espera_y_despues_se_apaga()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);

        var espera = buscador.EsperarAsync(50, TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        Assert.True(fuente.Activa);
        fuente.Llega(Ahora(15));
        await espera;

        Assert.False(fuente.Activa);
        Assert.Equal(1, fuente.Inicios);
    }

    [Fact]
    public void Iniciar_dos_veces_no_vuelve_a_pedir_el_GPS_y_Detener_lo_apaga()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);

        buscador.Iniciar();
        buscador.Iniciar();
        buscador.Detener();

        Assert.Equal(1, fuente.Inicios);
        Assert.False(buscador.Activo);
    }

    [Fact]
    public async Task Si_el_GPS_esta_apagado_el_mensaje_llega_tal_cual_a_quien_lo_pidio()
    {
        var fuente = new FuenteFalsa { FalloAlIniciar = new InvalidOperationException("El GPS está apagado.") };
        var buscador = new BuscadorDeUbicacion(fuente);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => buscador.EsperarAsync(50));

        Assert.Equal("El GPS está apagado.", ex.Message);
    }

    [Fact]
    public async Task Marca_las_ubicaciones_simuladas_para_que_el_servidor_las_rechace()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);
        buscador.Iniciar();
        fuente.Llega(Ahora(5, falsa: true));

        var lectura = await buscador.EsperarAsync(50, TimeSpan.FromSeconds(2));

        Assert.True(lectura!.EsFalsa);
    }

    [Fact]
    public async Task Avisa_el_avance_mientras_espera()
    {
        var fuente = new FuenteFalsa();
        var buscador = new BuscadorDeUbicacion(fuente);
        var avisos = new List<string>();
        buscador.Progreso = avisos.Add;
        buscador.Iniciar();
        fuente.Llega(Ahora(80));

        await buscador.EsperarAsync(50, TimeSpan.FromMilliseconds(250));

        Assert.Contains(avisos, a => a.Contains("±80 m"));
    }
}
