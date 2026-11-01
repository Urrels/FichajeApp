using System.Net;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Casos límite de la sincronización de feriados (auditoría)
public class FeriadosCasosLimiteTests
{
    private static FeriadoExterno Ext(int anio, int mes, int dia, string nombre = "Feriado", string tipo = "inamovible") =>
        new(new DateTime(anio, mes, dia), nombre, tipo);

    private static List<FeriadoExterno> Seis2026(params FeriadoExterno[] extra) => new List<FeriadoExterno>
    {
        Ext(2026, 1, 1), Ext(2026, 3, 24), Ext(2026, 5, 1), Ext(2026, 5, 25), Ext(2026, 7, 9), Ext(2026, 12, 25)
    }.Concat(extra).ToList();

    private static Entorno Con(List<FeriadoExterno> anio2026)
    {
        var e = new Entorno();
        e.Reloj.Ahora = new DateTime(2026, 10, 5, 12, 0, 0);
        e.ProveedorFeriados.PorAnio[2026] = anio2026;
        return e;
    }

    [Fact]
    public async Task UnFeriadoFuturoQueLaApiMovio_SeRetiraYSeAgregaElNuevo()
    {
        var e = Con(Seis2026(Ext(2026, 12, 8, "Inmaculada", "trasladable")));
        await e.Feriados.SincronizarAsync("a");

        e.ProveedorFeriados.PorAnio[2026] = Seis2026(Ext(2026, 12, 7, "Inmaculada", "trasladable"));
        var r = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(1, r.Agregados);
        Assert.Equal(1, r.Retirados);
        Assert.DoesNotContain(e.LiquidacionRepo.Feriados, f => f.Fecha == new DateTime(2026, 12, 8));
        Assert.Contains(e.LiquidacionRepo.Feriados, f => f.Fecha == new DateTime(2026, 12, 7));
    }

    [Fact]
    public async Task UnFeriadoPasado_NuncaSeRetira()
    {
        var e = Con(Seis2026());
        await e.Feriados.SincronizarAsync("a");
        e.ProveedorFeriados.PorAnio[2026] = Seis2026().Where(f => f.Fecha.Month != 1).ToList();   // enero ya pasó

        var r = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(0, r.Retirados);
        Assert.Contains(e.LiquidacionRepo.Feriados, f => f.Fecha == new DateTime(2026, 1, 1));
    }

    [Fact]
    public async Task UnaRespuestaCortaDeLaApi_NoBorraNada()
    {
        var e = Con(Seis2026());
        await e.Feriados.SincronizarAsync("a");
        e.ProveedorFeriados.PorAnio[2026] = new();   // la API contestó 200 con una lista vacía

        var r = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(0, r.Retirados);
        Assert.Equal(6, e.LiquidacionRepo.Feriados.Count);
    }

    [Fact]
    public async Task EliminarUnoOficial_LoExcluye_YLaSincronizacionNoLoVuelveATraer()
    {
        var e = Con(Seis2026());
        await e.Feriados.SincronizarAsync("a");

        await e.Liquidacion.EliminarFeriadoAsync("a", new DateTime(2026, 5, 25));
        await e.Feriados.SincronizarAsync("a");

        Assert.Equal(Feriado.OrigenExcluido, e.LiquidacionRepo.Feriados.Single(f => f.Fecha == new DateTime(2026, 5, 25)).Origen);
        Assert.DoesNotContain(await e.Liquidacion.ListarFeriadosAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)), f => f.Fecha == new DateTime(2026, 5, 25));
        Assert.DoesNotContain(await e.Feriados.ObtenerAnioAsync(2026), f => f.Fecha == "2026-05-25");
    }

    [Fact]
    public async Task UnFeriadoExcluido_NoSumaRecargoEnLaLiquidacion()
    {
        var e = Con(Seis2026(Ext(2026, 10, 5, "Feriado del lunes")));
        await e.Feriados.SincronizarAsync("a");
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 5, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 5, 16, 0, 0));
        var dia = new DateTime(2026, 10, 5);
        Assert.Equal(8 * 60, (await e.Liquidacion.CalcularAsync(dia, dia, "Ana Empleada")).Single().MinFeriado);

        await e.Liquidacion.EliminarFeriadoAsync("a", dia);

        Assert.Equal(0, (await e.Liquidacion.CalcularAsync(dia, dia, "Ana Empleada")).Single().MinFeriado);
    }

    [Fact]
    public async Task EliminarUnoManual_LoBorraDeVerdad()
    {
        var e = Con(Seis2026());
        await e.Liquidacion.GuardarFeriadoAsync("a", new DateTime(2026, 8, 17), "Cumpleaños de la empresa");

        await e.Liquidacion.EliminarFeriadoAsync("a", new DateTime(2026, 8, 17));

        Assert.Empty(e.LiquidacionRepo.Feriados);
    }

    [Fact]
    public async Task GuardarManual_RechazaNombreVacioYFechasAbsurdas()
    {
        var e = Con(Seis2026());

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarFeriadoAsync("a", new DateTime(2026, 8, 17), "   "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarFeriadoAsync("a", default, "Algo"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarFeriadoAsync("a", new DateTime(3000, 1, 1), "Algo"));
    }

    [Fact]
    public async Task ElNombreOficial_SeLimpiaYSeAcota()
    {
        var e = Con(new() { Ext(2026, 5, 1, "  Día\u0007 del\nTrabajo " + new string('x', 300)), Ext(2026, 5, 2, "   ") });

        await e.Feriados.SincronizarAsync("a");

        var largo = e.LiquidacionRepo.Feriados.Single(f => f.Fecha == new DateTime(2026, 5, 1));
        Assert.Equal(100, largo.Descripcion.Length);
        Assert.DoesNotContain(largo.Descripcion, c => char.IsControl(c));
        Assert.Equal("Feriado", e.LiquidacionRepo.Feriados.Single(f => f.Fecha == new DateTime(2026, 5, 2)).Descripcion);
    }

    [Fact]
    public async Task UnDatoDeOtroAnio_NoSeAgrega()
    {
        var e = Con(Seis2026(Ext(2025, 12, 25)));

        await e.Feriados.SincronizarAsync("a");

        Assert.DoesNotContain(e.LiquidacionRepo.Feriados, f => f.Fecha.Year == 2025);
    }

    [Fact]
    public async Task SiFallaElAnioActual_AunqueElSiguienteResponda_EsUnError()
    {
        var e = new Entorno();
        e.Reloj.Ahora = new DateTime(2026, 10, 5, 12, 0, 0);
        e.ProveedorFeriados.PorAnio[2027] = new() { Ext(2027, 1, 1) };   // 2026 no responde

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Feriados.SincronizarAsync("a"));
        Assert.Empty(e.LiquidacionRepo.Feriados);
    }

    [Fact]
    public void Fusion_ToleraFechasRepetidasEnLaPlanilla()
    {
        var actuales = new[]
        {
            new Feriado { Fecha = new DateTime(2026, 5, 1), Descripcion = "A", Origen = Feriado.OrigenManual },
            new Feriado { Fecha = new DateTime(2026, 5, 1), Descripcion = "B", Origen = Feriado.OrigenManual }
        };
        var oficiales = new[] { new Feriado { Fecha = new DateTime(2026, 5, 1), Descripcion = "Oficial", Origen = Feriado.OrigenApi } };

        var plan = FusionFeriados.Planificar(actuales, oficiales, new DateTime(2026, 1, 1));

        Assert.Equal(1, plan.Omitidos);
        Assert.Single(FusionFeriados.Aplicar(actuales, plan));
    }

    [Fact]
    public async Task ElProveedor_DescartaItemsNulosOIncompletos_SinRomper()
    {
        var json = "[null,{\"fecha\":null,\"tipo\":\"x\",\"nombre\":\"y\"},{\"fecha\":\"2026-05-01\",\"tipo\":null,\"nombre\":null},{\"fecha\":\"2026-13-45\"}]";
        var http = new HttpClient(new Fijo(json)) { BaseAddress = new Uri("https://api.test/") };

        var lista = await new ArgentinaDatosProveedor(http).ObtenerAsync(2026);

        Assert.Single(lista);
        Assert.Equal("", lista[0].Nombre);
    }

    private class Fijo : HttpMessageHandler
    {
        private readonly string _json;
        public Fijo(string json) => _json = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json") });
    }
}
