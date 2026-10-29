using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Apariencia;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Tests;

public class ReglasAparienciaTests
{
    [Theory]
    [InlineData("#1b5e9b", "#1B5E9B")]
    [InlineData("1B5E9B", "#1B5E9B")]
    [InlineData("  #ffffff ", "#FFFFFF")]
    public void NormalizarColor_AceptaConOSinNumeralYEnMinusculas(string entrada, string esperado) =>
        Assert.Equal(esperado, ReglasApariencia.NormalizarColor(entrada));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("rojo")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void NormalizarColor_RechazaLoQueNoEsUnColorHex(string? entrada) =>
        Assert.Null(ReglasApariencia.NormalizarColor(entrada));

    [Fact]
    public void Contraste_BlancoSobreNegroEs21_YColoresIgualesEs1()
    {
        Assert.Equal(21.0, ReglasApariencia.Contraste("#FFFFFF", "#000000"), 1);
        Assert.Equal(1.0, ReglasApariencia.Contraste("#336699", "#336699"), 3);
    }

    [Fact]
    public void Contraste_NoDependeDelOrden() =>
        Assert.Equal(ReglasApariencia.Contraste("#1B5E9B", "#FFFFFF"), ReglasApariencia.Contraste("#FFFFFF", "#1B5E9B"), 6);

    [Theory]
    [InlineData("#FFEB3B", false)] // amarillo con texto blanco: no se lee
    [InlineData("#1B5E9B", true)]  // azul oscuro con texto blanco: se lee
    [InlineData("#455A64", true)]
    public void ElTextoBlancoSeLeeSoloSobreColoresOscuros(string fondo, bool legible) =>
        Assert.Equal(legible, ReglasApariencia.Contraste(fondo, "#FFFFFF") >= ReglasApariencia.ContrasteMinimo);

    [Fact]
    public void DetectarFormato_PorLosPrimerosBytes_NoPorLaExtension()
    {
        Assert.Equal(ReglasApariencia.FormatoLogo.Png, ReglasApariencia.DetectarFormato(Imagenes.Png));
        Assert.Equal(ReglasApariencia.FormatoLogo.Jpeg, ReglasApariencia.DetectarFormato(Imagenes.Jpeg));
        Assert.Equal(ReglasApariencia.FormatoLogo.Desconocido, ReglasApariencia.DetectarFormato("<svg/>"u8));
        Assert.Equal(ReglasApariencia.FormatoLogo.Desconocido, ReglasApariencia.DetectarFormato(Array.Empty<byte>()));
    }

    [Fact]
    public void LaVersionDelLogo_CambiaSiCambiaLaImagen()
    {
        Assert.Equal(ReglasApariencia.VersionDe(Imagenes.Png), ReglasApariencia.VersionDe(Imagenes.Png));
        Assert.NotEqual(ReglasApariencia.VersionDe(Imagenes.Png), ReglasApariencia.VersionDe(Imagenes.Jpeg));
    }
}

// Bytes mínimos que empiezan como una imagen real (alcanza para detectar el formato)
public static class Imagenes
{
    public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
}

public class AparienciaServiceTests
{
    private static GuardarAparienciaRequest Pedido(string primario = "#1B5E9B", string texto = "#FFFFFF", string acento = "#F5A623") =>
        new() { Nombre = "Mi Empresa", ColorPrimario = primario, ColorPrimarioTexto = texto, ColorAcento = acento };

    [Fact]
    public async Task Guardar_NormalizaLosColores_YQuedaEnLaBitacora()
    {
        var e = new Entorno();

        await e.Apariencia.GuardarAsync("admin@test.com", Pedido(primario: "1b5e9b"));

        var pub = await e.Apariencia.ObtenerPublicaAsync();
        Assert.Equal("#1B5E9B", pub.ColorPrimario);
        Assert.Equal("Mi Empresa", pub.Nombre);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion == "APARIENCIA_MODIFICADA" && b.Usuario == "admin@test.com");
    }

    [Fact]
    public async Task Guardar_ConValoresVacios_VuelveAlValorDelCliente()
    {
        var e = new Entorno();
        await e.Apariencia.GuardarAsync("a", Pedido());

        await e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest());

        var pub = await e.Apariencia.ObtenerPublicaAsync();
        Assert.Null(pub.Nombre);
        Assert.Null(pub.ColorPrimario);
        Assert.Null(pub.ColorAcento);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#12")]
    public async Task Guardar_RechazaUnColorInvalido(string color)
    {
        var e = new Entorno();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apariencia.GuardarAsync("a", Pedido(acento: color)));
        Assert.Contains("#RRGGBB", ex.Message);
        Assert.Equal(0, e.AparienciaRepo.Guardados);
    }

    [Fact]
    public async Task Guardar_RechazaUnTextoQueNoSeLeeSobreElColorPrincipal()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apariencia.GuardarAsync("a", Pedido(primario: "#FFEB3B", texto: "#FFFFFF")));
        // sin texto definido se usa blanco: el mismo amarillo también se rechaza
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest { ColorPrimario = "#FFEB3B" }));
        // amarillo con texto oscuro sí
        await e.Apariencia.GuardarAsync("a", Pedido(primario: "#FFEB3B", texto: "#000000"));
    }

    [Fact]
    public async Task Guardar_RechazaUnNombreMuyLargo()
    {
        var e = new Entorno();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest { Nombre = new string('x', 41) }));
    }

    [Fact]
    public async Task Logo_SeAceptaEnBase64_ConOSinPrefijo_YSeSirveConSuTipo()
    {
        var e = new Entorno();
        var pedido = Pedido();
        pedido.LogoBase64 = "data:image/png;base64," + Convert.ToBase64String(Imagenes.Png);

        await e.Apariencia.GuardarAsync("a", pedido);

        var pub = await e.Apariencia.ObtenerPublicaAsync();
        Assert.True(pub.TieneLogo);
        Assert.Equal(ReglasApariencia.VersionDe(Imagenes.Png), pub.VersionLogo);

        var logo = await e.Apariencia.ObtenerLogoAsync();
        Assert.Equal("image/png", logo!.Value.Mime);
        Assert.Equal(Imagenes.Png, logo.Value.Bytes);

        pedido.LogoBase64 = Convert.ToBase64String(Imagenes.Jpeg);
        await e.Apariencia.GuardarAsync("a", pedido);
        Assert.Equal("image/jpeg", (await e.Apariencia.ObtenerLogoAsync())!.Value.Mime);
    }

    [Fact]
    public async Task Logo_SeRechazaSiNoEsImagen_SiPesaDemas_OSiNoEsBase64()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest { LogoBase64 = Convert.ToBase64String("<svg/>"u8.ToArray()) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest { LogoBase64 = "esto no es base64!!" }));

        var pesado = Imagenes.Png.Concat(new byte[ReglasApariencia.TamanioMaximoLogoBytes]).ToArray();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Apariencia.GuardarAsync("a", new GuardarAparienciaRequest { LogoBase64 = Convert.ToBase64String(pesado) }));
        Assert.Contains("pesado", ex.Message);
        Assert.Null(await e.Apariencia.ObtenerLogoAsync());
    }

    [Fact]
    public async Task Logo_SeConservaSiNoSeMandaUnoNuevo_YSeQuitaSiSePide()
    {
        var e = new Entorno();
        var pedido = Pedido();
        pedido.LogoBase64 = Convert.ToBase64String(Imagenes.Png);
        await e.Apariencia.GuardarAsync("a", pedido);

        await e.Apariencia.GuardarAsync("a", Pedido(primario: "#455A64")); // solo cambia un color
        Assert.True((await e.Apariencia.ObtenerPublicaAsync()).TieneLogo);

        var quitar = Pedido();
        quitar.QuitarLogo = true;
        await e.Apariencia.GuardarAsync("a", quitar);
        Assert.False((await e.Apariencia.ObtenerPublicaAsync()).TieneLogo);
        Assert.Null(await e.Apariencia.ObtenerLogoAsync());
    }

    [Fact]
    public async Task UnLogoGuardadoConBytesDeOtroFormato_NoSeSirve()
    {
        var e = new Entorno();
        await e.AparienciaRepo.GuardarAsync(new AparienciaGuardada { Logo = "<svg/>"u8.ToArray() }); // editado a mano en la planilla

        Assert.Null(await e.Apariencia.ObtenerLogoAsync());
    }
}

public class AparienciaHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public AparienciaHttpTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task LaApariencia_SeLeeSinIniciarSesion_YElLogoNoExisteTodavia()
    {
        // instancia propia: otro test de esta clase puede haber guardado un logo en la compartida
        using var propia = new ApiFactory();
        var anonimo = propia.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/api/apariencia")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/api/apariencia/logo")).StatusCode);
    }

    [Fact]
    public async Task SoloQuienTieneElPermiso_PuedeCambiarLaApariencia()
    {
        var cuerpo = new { nombre = "X", colorPrimario = "#1B5E9B" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PutAsJsonAsync("/api/admin/apariencia", cuerpo)).StatusCode);
        foreach (var (email, pin) in new[] { ("ana@test.com", Entorno.PinAna), ("beto@test.com", Entorno.PinBeto) })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteDe(email, pin)).PutAsJsonAsync("/api/admin/apariencia", cuerpo)).StatusCode);
    }

    [Fact]
    public async Task ElAdministrador_CambiaLaApariencia_YTodosLaVenAlInstante()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PutAsJsonAsync("/api/admin/apariencia", new
        {
            nombre = "Gimnasio Leo", colorPrimario = "#8E24AA", colorPrimarioTexto = "#FFFFFF", colorAcento = "#FFB300",
            logoBase64 = Convert.ToBase64String(Imagenes.Png)
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var publica = await (await _fabrica.CreateClient().GetAsync("/api/apariencia")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Gimnasio Leo", publica.GetProperty("nombre").GetString());
        Assert.Equal("#8E24AA", publica.GetProperty("colorPrimario").GetString());
        Assert.True(publica.GetProperty("tieneLogo").GetBoolean());

        var logo = await _fabrica.CreateClient().GetAsync("/api/apariencia/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Imagenes.Png, await logo.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnaRegla_SeDevuelveComo400ConMensaje()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PutAsJsonAsync("/api/admin/apariencia", new { colorPrimario = "#FFEB3B" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("no se lee bien", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
    }
}
