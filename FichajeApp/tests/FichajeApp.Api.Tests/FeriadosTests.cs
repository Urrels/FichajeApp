using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class FeriadosTests
{
    private static FeriadoExterno Ext(int mes, int dia, string nombre, string tipo, int anio = 2026) => new(new DateTime(anio, mes, dia), nombre, tipo);

    private static Entorno ConApi()
    {
        var e = new Entorno();
        e.Reloj.Ahora = new DateTime(2026, 10, 5, 12, 0, 0);
        e.ProveedorFeriados.PorAnio[2026] = new() { Ext(10, 12, "Día del Respeto a la Diversidad Cultural", "trasladable"), Ext(11, 23, "Puente turístico", "puente") };
        e.ProveedorFeriados.PorAnio[2027] = new() { Ext(1, 1, "Año Nuevo", "inamovible", 2027) };
        return e;
    }

    [Fact]
    public async Task Sincronizar_AgregaLosFeriadosDelAnioActualYElSiguiente()
    {
        var e = ConApi();

        var r = await e.Feriados.SincronizarAsync("admin@test.com");

        Assert.Equal(3, r.Agregados);
        Assert.Equal(3, e.LiquidacionRepo.Feriados.Count);
        Assert.All(e.LiquidacionRepo.Feriados, f => Assert.Equal(Feriado.OrigenApi, f.Origen));
    }

    [Fact]
    public async Task Sincronizar_DosVeces_NoDuplicaNiPisaLoManual()
    {
        var e = ConApi();
        e.LiquidacionRepo.Feriados.Add(new Feriado { Fecha = new DateTime(2026, 10, 12), Descripcion = "Nombre propio", Origen = Feriado.OrigenManual });

        await e.Feriados.SincronizarAsync("a");
        var segunda = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(0, segunda.Agregados + segunda.Actualizados);
        Assert.Equal(3, e.LiquidacionRepo.Feriados.Count);
        Assert.Equal("Nombre propio", e.LiquidacionRepo.Feriados.Single(f => f.Fecha == new DateTime(2026, 10, 12)).Descripcion);
    }

    [Fact]
    public async Task Sincronizar_ActualizaLoQueVinoDeLaApiSiCambio()
    {
        var e = ConApi();
        await e.Feriados.SincronizarAsync("a");
        e.ProveedorFeriados.PorAnio[2026][0] = Ext(10, 12, "Nuevo nombre", "trasladable");

        var r = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(1, r.Actualizados);
        Assert.Equal("Nuevo nombre", e.LiquidacionRepo.Feriados.Single(f => f.Fecha == new DateTime(2026, 10, 12)).Descripcion);
    }

    [Fact]
    public async Task Sincronizar_ElAnioSiguienteSinPublicar_NoEsError()
    {
        var e = ConApi();
        e.ProveedorFeriados.PorAnio.Remove(2027);

        var r = await e.Feriados.SincronizarAsync("a");

        Assert.Equal(2, r.Agregados);
    }

    [Fact]
    public async Task Sincronizar_SinConexion_DaUnMensajeClaroYNoTocaLaPlanilla()
    {
        var e = ConApi();
        e.ProveedorFeriados.Falla = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Feriados.SincronizarAsync("a"));

        Assert.Contains("internet", ex.Message);
        Assert.Empty(e.LiquidacionRepo.Feriados);
    }

    [Fact]
    public async Task ObtenerAnio_UsaCacheHastaQueSeInvalida()
    {
        var e = ConApi();
        await e.Feriados.SincronizarAsync("a");
        Assert.Equal(2, (await e.Feriados.ObtenerAnioAsync(2026)).Count);

        e.LiquidacionRepo.Feriados.Add(new Feriado { Fecha = new DateTime(2026, 12, 8), Descripcion = "Inmaculada" });
        Assert.Equal(2, (await e.Feriados.ObtenerAnioAsync(2026)).Count);   // sigue en cache

        e.Feriados.Invalidar();
        Assert.Equal(3, (await e.Feriados.ObtenerAnioAsync(2026)).Count);
    }

    [Fact]
    public async Task ObtenerAnio_MarcaQueElPuenteNoPagaRecargoPorDefecto()
    {
        var e = ConApi();
        await e.Feriados.SincronizarAsync("a");

        var lista = await e.Feriados.ObtenerAnioAsync(2026);

        Assert.True(lista.Single(f => f.Tipo == "trasladable").PagaRecargo);
        Assert.False(lista.Single(f => f.Tipo == "puente").PagaRecargo);
    }

    [Fact]
    public async Task Liquidacion_ElPuenteNoSumaRecargo_AMenosQueSeActive()
    {
        var e = ConApi();
        // Ana trabaja 8 h el lunes 5/10, que es un puente
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 5, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 5, 16, 0, 0));
        e.LiquidacionRepo.Feriados.Add(new Feriado { Fecha = new DateTime(2026, 10, 5), Descripcion = "Puente", Tipo = "puente", Origen = Feriado.OrigenApi });
        var dia = new DateTime(2026, 10, 5);

        var sinRecargo = (await e.Liquidacion.CalcularAsync(dia, dia, "Ana Empleada")).Single();
        Assert.Equal(0, sinRecargo.MinFeriado);

        e.LiquidacionRepo.Parametros.PagarPuentes = true;
        var conRecargo = (await e.Liquidacion.CalcularAsync(dia, dia, "Ana Empleada")).Single();
        Assert.Equal(8 * 60, conRecargo.MinFeriado);
    }

    [Fact]
    public async Task Liquidacion_UnFeriadoObligatorioSiSumaRecargo()
    {
        var e = ConApi();
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 5, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 5, 16, 0, 0));
        e.LiquidacionRepo.Feriados.Add(new Feriado { Fecha = new DateTime(2026, 10, 5), Descripcion = "Feriado", Tipo = "inamovible", Origen = Feriado.OrigenApi });
        var dia = new DateTime(2026, 10, 5);

        Assert.Equal(8 * 60, (await e.Liquidacion.CalcularAsync(dia, dia, "Ana Empleada")).Single().MinFeriado);
    }

    [Fact]
    public async Task ProveedorArgentinaDatos_LeeLaRespuestaDeLaApi()
    {
        var json = "[{\"fecha\":\"2026-01-01\",\"tipo\":\"inamovible\",\"nombre\":\"Año Nuevo\"},{\"fecha\":\"2026-03-23\",\"tipo\":\"puente\",\"nombre\":\"Día no laborable con fines turísticos\"},{\"fecha\":\"basura\",\"tipo\":\"x\",\"nombre\":\"y\"}]";
        var http = new HttpClient(new RespuestaFija(json)) { BaseAddress = new Uri("https://api.test/") };

        var lista = await new ArgentinaDatosProveedor(http).ObtenerAsync(2026);

        Assert.Equal(2, lista.Count);
        Assert.Equal("puente", lista[1].Tipo);
        Assert.Equal(new DateTime(2026, 1, 1), lista[0].Fecha);
    }

    private class RespuestaFija : HttpMessageHandler
    {
        private readonly string _json;
        public RespuestaFija(string json) => _json = json;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json") });
    }
}

public class FeriadosApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public FeriadosApiTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task Listar_SinSesion_DaNoAutorizado()
    {
        var r = await _fabrica.CreateClient().GetAsync("/api/feriados?anio=2026");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Listar_UnEmpleadoVeLosFeriadosDelAnio()
    {
        _fabrica.Datos.LiquidacionRepo.Feriados.Add(new Feriado { Fecha = new DateTime(2026, 5, 1), Descripcion = "Día del Trabajador", Tipo = "inamovible", Origen = Feriado.OrigenApi });
        var cliente = await ClienteDe("ana@test.com", Entorno.PinAna);

        var r = await cliente.GetAsync("/api/feriados?anio=2026");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var lista = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(lista.EnumerateArray(), f => f.GetProperty("fecha").GetString() == "2026-05-01");
    }

    [Fact]
    public async Task ActualizarAhora_ExigeElPermisoDeLiquidar()
    {
        var empleado = await ClienteDe("ana@test.com", Entorno.PinAna);
        Assert.Equal(HttpStatusCode.Forbidden, (await empleado.PostAsync("/api/admin/liquidacion/feriados/sincronizar", null)).StatusCode);

        _fabrica.Datos.ProveedorFeriados.PorAnio[_fabrica.Datos.Reloj.Ahora.Year] = new() { new(new DateTime(_fabrica.Datos.Reloj.Ahora.Year, 5, 25), "Revolución de Mayo", "inamovible") };
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var r = await admin.PostAsync("/api/admin/liquidacion/feriados/sincronizar", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(_fabrica.Datos.LiquidacionRepo.Feriados, f => f.Descripcion == "Revolución de Mayo");
    }
}
