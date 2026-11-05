using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Tests;

public class AlmacenPerfilesTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "perfiles-" + Guid.NewGuid().ToString("N"));
    private readonly AlmacenPerfilesEnDisco _almacen;

    public AlmacenPerfilesTests() => _almacen = new AlmacenPerfilesEnDisco(_carpeta);
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    internal static byte[] Jpg(int extra = 50) => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(new byte[extra]).ToArray();
    internal static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    [Fact]
    public async Task GuardaYLee_LaFotoDeUnaPersona()
    {
        await _almacen.GuardarAsync(2, Jpg());

        var foto = await _almacen.LeerAsync(2);

        Assert.NotNull(foto);
        Assert.Equal("image/jpeg", foto!.Value.Mime);
        Assert.Equal(Jpg(), foto.Value.Bytes);
        Assert.Null(await _almacen.LeerAsync(3));
    }

    [Fact]
    public async Task UnaFotoNuevaReemplazaALaAnterior_AunqueCambieElFormato()
    {
        await _almacen.GuardarAsync(2, Jpg());
        await _almacen.GuardarAsync(2, Png());

        var foto = await _almacen.LeerAsync(2);

        Assert.Equal("image/png", foto!.Value.Mime);
        Assert.Single(Directory.GetFiles(_carpeta), f => f.EndsWith(".jpg") || f.EndsWith(".png"));
    }

    [Fact]
    public async Task LasVersiones_SoloIncluyenAQuienTieneFoto_YCambianAlGuardarOtra()
    {
        await _almacen.GuardarAsync(2, Jpg());
        await _almacen.GuardarAsync(5, Png());
        var antes = (await _almacen.VersionesAsync())[2];

        File.SetLastWriteTimeUtc(Path.Combine(_carpeta, "2.jpg"), DateTime.UtcNow.AddMinutes(-10));   // la anterior es "vieja"
        var vieja = (await _almacen.VersionesAsync())[2];
        await _almacen.GuardarAsync(2, Jpg(80));
        var despues = (await _almacen.VersionesAsync())[2];

        var versiones = await _almacen.VersionesAsync();
        Assert.Equal(new[] { 2, 5 }, versiones.Keys.OrderBy(k => k));
        Assert.True(despues > vieja);
        Assert.True(antes >= vieja);
    }

    [Fact]
    public async Task EliminarBorra_YSeIgnoraLoQueNoEsDelAlmacen()
    {
        await _almacen.GuardarAsync(2, Jpg());
        await File.WriteAllTextAsync(Path.Combine(_carpeta, "notas.txt"), "no es una foto");
        await File.WriteAllTextAsync(Path.Combine(_carpeta, "2.jpg.tmp"), "a medio escribir");

        Assert.True(await _almacen.EliminarAsync(2));
        Assert.False(await _almacen.EliminarAsync(2));
        Assert.Empty(await _almacen.VersionesAsync());
    }
}

public class FotoDePerfilApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public FotoDePerfilApiTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    private static object Foto(byte[] bytes) => new { fotoBase64 = Convert.ToBase64String(bytes) };

    [Fact]
    public async Task SinSesion_NoSeVeNiSeCambia()
    {
        var anonimo = _fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/perfil/versiones")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/perfil/2/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/perfil/foto", Foto(AlmacenPerfilesTests.Jpg()))).StatusCode);
    }

    [Fact]
    public async Task CadaPersonaCambiaSuPropiaFoto_YOtraCuentaLaVe()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);

        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/perfil/foto", Foto(AlmacenPerfilesTests.Jpg()))).StatusCode);

        var ver = await beto.GetAsync("/api/perfil/2/foto");
        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
        Assert.Equal("image/jpeg", ver.Content.Headers.ContentType?.MediaType);
        var versiones = await beto.GetFromJsonAsync<Dictionary<string, long>>("/api/perfil/versiones");
        Assert.Contains("2", versiones!.Keys);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync("/api/perfil/3/foto")).StatusCode);   // Beto no cargó la suya
    }

    [Fact]
    public async Task QuitarLaPropiaFoto_LaBorra()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        await ana.PutAsJsonAsync("/api/perfil/foto", Foto(AlmacenPerfilesTests.Jpg()));

        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync("/api/perfil/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/perfil/2/foto")).StatusCode);
    }

    [Theory]
    [InlineData("no es base64 !!!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA")]   // base64 válido pero no es una imagen
    public async Task UnaImagenInvalida_SeRechazaConUnMensaje(string base64)
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        var r = await ana.PutAsJsonAsync("/api/perfil/foto", new { fotoBase64 = base64 });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()));
    }

    [Fact]
    public async Task UnaFotoDemasiadoPesada_SeRechaza()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        var r = await ana.PutAsJsonAsync("/api/perfil/foto", Foto(AlmacenPerfilesTests.Jpg(ReglasFotos.TamanioMaximoBytes + 10)));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("pesada", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElAdministradorCambiaLaFotoDeOtro_PeroUnEmpleadoNo()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/api/admin/usuarios/3/foto", Foto(AlmacenPerfilesTests.Jpg()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/usuarios/3/foto", Foto(AlmacenPerfilesTests.Png()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/perfil/3/foto")).StatusCode);
        Assert.Contains(_fabrica.Datos.BitacoraRepo.Datos, b => b.Accion.StartsWith("FOTO_PERFIL_MODIFICADA:"));

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/admin/usuarios/3/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/perfil/3/foto")).StatusCode);
    }

    [Fact]
    public async Task ParaUnUsuarioQueNoExiste_ElAdministradorRecibeUnMensajeClaro()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PutAsJsonAsync("/api/admin/usuarios/999/foto", Foto(AlmacenPerfilesTests.Jpg()));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("no existe", await r.Content.ReadAsStringAsync());
    }
}
