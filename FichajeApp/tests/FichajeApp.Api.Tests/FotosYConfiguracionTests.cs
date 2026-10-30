using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Tests;

public class ConfiguracionFichajeTests
{
    [Fact]
    public async Task SinNadaGuardado_RigeLoDeAppsettings_Y_SinFoto()
    {
        var e = new Entorno();
        var c = await e.ConfigFichaje.ObtenerAsync();
        Assert.Equal(ConfiguracionFichaje.UbicacionLibre, c.Ubicacion);
        Assert.Equal(ConfiguracionFichaje.FotoNo, c.Foto);

        var sedeFija = new ConfiguracionFichajeService(e.ConfigFichajeRepo, e.Bitacora,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new FichajeOptions { ModoUbicacion = "Sede" }), e.Roles);
        Assert.Equal(ConfiguracionFichaje.UbicacionSedeFija, (await sedeFija.ObtenerAsync()).Ubicacion);
    }

    [Fact]
    public async Task Guardar_NormalizaMayusculas_DejaBitacora_YSeVeEnSeguida()
    {
        var e = new Entorno();
        await e.ConfigFichaje.ObtenerAsync(); // queda en caché

        await e.ConfigFichaje.GuardarAsync("admin@test.com", new GuardarConfiguracionFichajeRequest { Ubicacion = "ninguna", Foto = "OBLIGATORIA" });

        var c = await e.ConfigFichaje.ObtenerAsync();
        Assert.Equal("Ninguna", c.Ubicacion);
        Assert.Equal("Obligatoria", c.Foto);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion.StartsWith("CONFIG_FICHAJE"));
    }

    [Theory]
    [InlineData("Cualquiera", "No")]
    [InlineData("Libre", "Siempre")]
    [InlineData(null, "No")]
    public async Task Guardar_RechazaOpcionesQueNoExisten(string? ubicacion, string foto)
    {
        var e = new Entorno();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.ConfigFichaje.GuardarAsync("a", new GuardarConfiguracionFichajeRequest { Ubicacion = ubicacion, Foto = foto }));
        Assert.Null(e.ConfigFichajeRepo.Datos);
    }
}

public class ReglasFotosTests
{
    [Fact]
    public void Decodificar_AceptaBase64YDataUrl_DePngOJpg()
    {
        Assert.Equal(Imagenes.Png, ReglasFotos.Decodificar(Convert.ToBase64String(Imagenes.Png)));
        Assert.Equal(Imagenes.Jpeg, ReglasFotos.Decodificar("data:image/jpeg;base64," + Convert.ToBase64String(Imagenes.Jpeg)));
    }

    [Fact]
    public void Decodificar_RechazaLoQueNoEsUnaFotoValida()
    {
        Assert.Throws<FormatException>(() => ReglasFotos.Decodificar("esto no es base64!!"));
        Assert.Throws<FormatException>(() => ReglasFotos.Decodificar(Convert.ToBase64String("<svg/>"u8.ToArray())));

        var pesada = new byte[ReglasFotos.TamanioMaximoBytes + 1];
        Imagenes.Jpeg.CopyTo(pesada, 0);
        Assert.Throws<FormatException>(() => ReglasFotos.Decodificar(Convert.ToBase64String(pesada)));
    }
}

public class AlmacenFotosEnDiscoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_fotos_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    [Fact]
    public async Task GuardaPorFecha_YLaLee_ConSuTipo()
    {
        var almacen = new AlmacenFotosEnDisco(_carpeta);

        var nombre = await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2026, 10, 29, 8, 30, 0));
        Assert.StartsWith("2026/10/29/", nombre);
        Assert.EndsWith(".jpg", nombre);

        var foto = await almacen.LeerAsync(nombre);
        Assert.Equal(Imagenes.Jpeg, foto!.Value.Bytes);
        Assert.Equal("image/jpeg", foto.Value.Mime);

        var png = await almacen.GuardarAsync(Imagenes.Png, new DateTime(2026, 10, 29));
        Assert.EndsWith(".png", png);
        Assert.Equal("image/png", (await almacen.LeerAsync(png))!.Value.Mime);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("2026/10/29/../../../x.jpg")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("2026/10/29/no-es-un-id.jpg")]
    [InlineData("")]
    public async Task NoLeeNadaQueNoSeaUnNombreGeneradoPorElAlmacen(string nombre)
    {
        var almacen = new AlmacenFotosEnDisco(_carpeta);
        await almacen.GuardarAsync(Imagenes.Jpeg, DateTime.Today);
        Assert.Null(await almacen.LeerAsync(nombre));
    }
}

public class FichajeConFotoTests
{
    private static FichajeContext Contexto(Entorno e, byte[]? foto = null, bool ubicacionFalsa = false) => new()
    {
        Empleado = e.Empleados.Datos.First(x => x.Email == "ana@test.com"),
        Latitud = -34.6, Longitud = -58.4, Precision = 10, EsUbicacionFalsa = ubicacionFalsa,
        Sitio = "Sucursal Centro", Foto = foto
    };

    private static Task Configurar(Entorno e, string ubicacion, string foto) =>
        e.ConfigFichaje.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = ubicacion, Foto = foto });

    [Fact]
    public async Task FotoObligatoria_SinFotoNoSeFicha_ConFotoSeGuardaYQuedaEnElFichaje()
    {
        var e = new Entorno();
        await Configurar(e, "Libre", "Obligatoria");

        var sin = await e.Fichaje.ValidarAsync(Contexto(e));
        Assert.False(sin.EsValido);
        Assert.Equal("FOTO_REQUERIDA", sin.Codigo);

        var ctx = Contexto(e, Imagenes.Jpeg);
        Assert.True((await e.Fichaje.ValidarAsync(ctx)).EsValido);
        var fichaje = await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx);

        Assert.NotNull(fichaje.Foto);
        Assert.Equal(Imagenes.Jpeg, e.Fotos.Datos[fichaje.Foto!]);
        Assert.Equal(fichaje.Foto, e.StorageFichajes.Guardados.Single().Foto);
    }

    [Fact]
    public async Task FotoOpcional_SeGuardaSiLlega_YNoEsObligatoria()
    {
        var e = new Entorno();
        await Configurar(e, "Libre", "Opcional");

        Assert.True((await e.Fichaje.ValidarAsync(Contexto(e))).EsValido);
        var ctx = Contexto(e, Imagenes.Png);
        var fichaje = await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx);
        Assert.NotNull(fichaje.Foto);
    }

    [Fact]
    public async Task SinFoto_UnaFotoQueLlegaSeIgnora()
    {
        var e = new Entorno();
        await Configurar(e, "Libre", "No");

        var ctx = Contexto(e, Imagenes.Jpeg);
        var fichaje = await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx);

        Assert.Null(fichaje.Foto);
        Assert.Empty(e.Fotos.Datos);
    }

    [Fact]
    public async Task UbicacionNinguna_NoValidaNiGuardaLaUbicacion()
    {
        var e = new Entorno();
        await Configurar(e, "Ninguna", "Obligatoria");

        // Aunque venga marcada como falsa, con "Ninguna" no se mira la ubicación
        var ctx = Contexto(e, Imagenes.Jpeg, ubicacionFalsa: true);
        Assert.True((await e.Fichaje.ValidarAsync(ctx)).EsValido);

        var fichaje = await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx);
        Assert.Equal(0, fichaje.Latitud);
        Assert.Equal(0, fichaje.Longitud);
        Assert.Equal("Sin ubicación", fichaje.Lugar);
        Assert.Null(fichaje.Precision);
    }

    [Fact]
    public async Task UbicacionLibre_SigueRechazandoLaUbicacionSimulada()
    {
        var e = new Entorno();
        await Configurar(e, "Libre", "No");

        var r = await e.Fichaje.ValidarAsync(Contexto(e, ubicacionFalsa: true));
        Assert.False(r.EsValido);
        Assert.Contains("simulada", r.Mensaje);
    }

    [Fact]
    public async Task AlternaEntradaYSalida()
    {
        var e = new Entorno();
        var ctx = Contexto(e);
        Assert.Equal("Entrada", (await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx)).TipoFichaje);
        Assert.Equal("Salida", (await e.Fichaje.RegistrarAsync(ctx.Empleado!, ctx)).TipoFichaje);
    }
}

// Por HTTP, con la API real en memoria
public class FotosApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public FotosApiTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    private static object Fichaje(string? foto) => new
    {
        latitud = -34.6, longitud = -58.4, precision = 10, esUbicacionFalsa = false, motivo = "Visita", fotoBase64 = foto
    };

    [Fact]
    public async Task ElAdministradorConfigura_ElEmpleadoLaConsulta_YLaFotoObligatoriaSeExige()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        // Un empleado no puede cambiarla
        var denegado = await ana.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "Obligatoria" });
        Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);

        var ok = await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "Obligatoria" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var config = await (await ana.GetAsync("/api/fichaje/configuracion")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Obligatoria", config.GetProperty("foto").GetString());

        // Sin foto: 400 con código para que la app sepa que falta
        var sinFoto = await ana.PostAsJsonAsync("/api/fichaje", Fichaje(null));
        Assert.Equal(HttpStatusCode.BadRequest, sinFoto.StatusCode);
        Assert.Equal("FOTO_REQUERIDA", (await sinFoto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());

        // Foto inválida
        var invalida = await ana.PostAsJsonAsync("/api/fichaje", Fichaje(Convert.ToBase64String("<svg/>"u8.ToArray())));
        Assert.Equal("FOTO_INVALIDA", (await invalida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());

        // Con foto: se registra y la foto queda guardada
        var antes = _fabrica.Datos.Fotos.Datos.Count;
        var conFoto = await ana.PostAsJsonAsync("/api/fichaje", Fichaje(Convert.ToBase64String(Imagenes.Jpeg)));
        Assert.Equal(HttpStatusCode.OK, conFoto.StatusCode);
        Assert.Equal(antes + 1, _fabrica.Datos.Fotos.Datos.Count);
    }

    [Fact]
    public async Task LaFotoSoloLaVeUnSupervisor_YUnaQueNoExisteDa404()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/supervisor/foto?archivo=2026/10/29/x.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync("/api/supervisor/foto?archivo=2026/10/29/x.jpg")).StatusCode);

        var nombre = await _fabrica.Datos.Fotos.GuardarAsync(Imagenes.Jpeg, DateTime.Today);
        var r = await beto.GetAsync("/api/supervisor/foto?archivo=" + Uri.EscapeDataString(nombre));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
    }
}
