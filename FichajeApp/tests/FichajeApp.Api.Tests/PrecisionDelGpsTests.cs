using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Adentro de un edificio el GPS suele dar de 30 a 150 m de error. El límite que acepta el servidor lo elige el administrador.
public class PrecisionDelGpsTests
{
    private static ConfiguracionFichajeService ServicioCon(Entorno e, double precisionDeAppsettings) =>
        new(e.ConfigFichajeRepo, e.Bitacora,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new FichajeOptions { PrecisionMaxMetros = precisionDeAppsettings }), e.Roles);

    private static FichajeContext Ctx(Entorno e, double? precision) => new()
    {
        Empleado = e.Empleados.Datos.First(x => x.Email == "ana@test.com"),
        Latitud = -34.6, Longitud = -58.4, Precision = precision, Sitio = "Sucursal Centro"
    };

    private static Task Guardar(Entorno e, int? precision) =>
        e.ConfigFichaje.GuardarAsync("admin@test.com",
            new GuardarConfiguracionFichajeRequest { Ubicacion = "Libre", Foto = "No", PrecisionMaxMetros = precision });

    [Theory]
    [InlineData(50)]
    [InlineData(120)]
    public async Task SinUnValorElegido_RigeLoDeAppsettings(double deAppsettings)
    {
        var e = new Entorno();

        var c = await ServicioCon(e, deAppsettings).ObtenerAsync();

        Assert.Equal((int)deAppsettings, c.PrecisionMaxMetros);
    }

    [Fact]
    public async Task UnValorGuardadoEnLaPlanillaEnCero_TambienUsaAppsettings()
    {
        var e = new Entorno();
        e.ConfigFichajeRepo.Datos = new ConfiguracionFichaje { Ubicacion = "Libre", Foto = "No", PrecisionMaxMetros = 0 };

        Assert.Equal(75, (await ServicioCon(e, 75).ObtenerAsync()).PrecisionMaxMetros);
    }

    [Fact]
    public async Task ElAdministradorLaElige_YSeConservaSiDespuesGuardaOtraCosa()
    {
        var e = new Entorno();

        await Guardar(e, 100);
        Assert.Equal(100, (await e.ConfigFichaje.ObtenerAsync()).PrecisionMaxMetros);

        // Guardar la foto o la ubicación sin tocar la precisión no la pisa
        await e.ConfigFichaje.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = "Ninguna", Foto = "Opcional" });
        Assert.Equal(100, (await e.ConfigFichaje.ObtenerAsync()).PrecisionMaxMetros);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion.Contains("100m"));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    public async Task UnValorFueraDeRango_SeRechaza_YNoCambiaNada(int valor)
    {
        var e = new Entorno();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Guardar(e, valor));

        Assert.Contains("entre 10 y 500", ex.Message);
        Assert.Null(e.ConfigFichajeRepo.Datos);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public async Task LosExtremosDelRango_SeAceptan(int valor)
    {
        var e = new Entorno();

        await Guardar(e, valor);

        Assert.Equal(valor, (await e.ConfigFichaje.ObtenerAsync()).PrecisionMaxMetros);
    }

    [Fact]
    public async Task ElFichajeUsaElLimiteConfigurado()
    {
        var e = new Entorno();

        // Con el límite de siempre (50 m), una lectura de ±80 m se rechaza y el mensaje dice los números
        var rechazada = await e.Fichaje.ValidarAsync(Ctx(e, 80));
        Assert.False(rechazada.EsValido);
        Assert.Contains("±80 m", rechazada.Mensaje);
        Assert.Contains("máximo 50 m", rechazada.Mensaje);

        // El administrador lo sube a 100 m: la misma lectura pasa
        await Guardar(e, 100);
        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, 80))).EsValido);
    }

    [Fact]
    public async Task ElLimiteEsInclusivo_YUnPocoMasSeRechaza()
    {
        var e = new Entorno();
        await Guardar(e, 100);

        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, 100))).EsValido);
        Assert.False((await e.Fichaje.ValidarAsync(Ctx(e, 100.5))).EsValido);
        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, null))).EsValido); // sin dato de precisión no se rechaza
    }

    [Fact]
    public async Task ConUbicacionNinguna_LaPrecisionNoImporta()
    {
        var e = new Entorno();
        await e.ConfigFichaje.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = "Ninguna", Foto = "No" });

        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, 5000))).EsValido);
    }
}

public class PrecisionDelGpsPorHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public PrecisionDelGpsPorHttpTests(ApiFactory fabrica) => _fabrica = fabrica;

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
    public async Task ElAdministradorLaCambia_LaAppLaLee_YElFichajeLaRespeta()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        var fichaje = new { latitud = -34.6, longitud = -58.4, precision = 90.0, esUbicacionFalsa = false, motivo = "Visita" };

        // Por defecto 50 m: ±90 m se rechaza
        var antes = await ana.PostAsJsonAsync("/api/fichaje", fichaje);
        Assert.Equal(HttpStatusCode.BadRequest, antes.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", precisionMaxMetros = 150 })).StatusCode);

        var config = await (await ana.GetAsync("/api/fichaje/configuracion")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(150, config.GetProperty("precisionMaxMetros").GetInt32());

        var despues = await ana.PostAsJsonAsync("/api/fichaje", fichaje);
        Assert.Equal(HttpStatusCode.OK, despues.StatusCode);
    }

    [Fact]
    public async Task UnValorInvalidoPorHttp_Es400_YUnEmpleadoNoPuedeCambiarla()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", precisionMaxMetros = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", precisionMaxMetros = 200 })).StatusCode);
    }
}
