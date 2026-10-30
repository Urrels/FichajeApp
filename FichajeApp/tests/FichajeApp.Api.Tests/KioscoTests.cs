using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class KioscoServiceTests
{
    private static int IdDe(Entorno e, string email) => e.Empleados.Datos.First(x => x.Email == email).Id;

    [Fact]
    public async Task Lista_ALosQuePuedenFichar_SinLaCuentaDeLaSucursal_YDiceQueLesToca()
    {
        var e = new Entorno();
        e.AgregarSucursal();

        var lista = await e.Kiosco.ListarAsync();

        Assert.Equal(new[] { "Admin Prueba", "Ana Empleada", "Beto Supervisor" }, lista.Select(x => x.Nombre));
        Assert.All(lista, x => Assert.Equal("Entrada", x.Proximo));

        await e.Kiosco.FicharAsync("Sucursal Centro", IdDe(e, "ana@test.com"), Entorno.PinAna, null);
        Assert.Equal("Salida", (await e.Kiosco.ListarAsync()).First(x => x.Nombre == "Ana Empleada").Proximo);
    }

    [Fact]
    public async Task Ficha_AlNombreDelEmpleado_ConLaSucursalComoLugar_YAlternaEntradaYSalida()
    {
        var e = new Entorno();
        var ana = IdDe(e, "ana@test.com");

        var entrada = await e.Kiosco.FicharAsync("Sucursal Centro", ana, Entorno.PinAna, null);
        var salida = await e.Kiosco.FicharAsync("Sucursal Centro", ana, Entorno.PinAna, null);

        Assert.True(entrada.Exito);
        Assert.Equal("Entrada", entrada.Fichaje!.TipoFichaje);
        Assert.Equal("Salida", salida.Fichaje!.TipoFichaje);
        Assert.Equal("Ana Empleada", entrada.Fichaje.NombreCompleto);
        Assert.Equal("Sucursal Centro", entrada.Fichaje.Lugar);
        Assert.Equal(0, entrada.Fichaje.Latitud);
        Assert.Contains("Entrada registrada: Ana Empleada", entrada.Mensaje);
        // No llena la bitácora con un LOGIN por cada fichaje
        Assert.DoesNotContain(e.BitacoraRepo.Datos, b => b.Accion == "LOGIN");
    }

    [Fact]
    public async Task UnPinIncorrecto_NoFicha_YTresErroresBloqueanAlEmpleado()
    {
        var e = new Entorno();
        var ana = IdDe(e, "ana@test.com");

        var mal = await e.Kiosco.FicharAsync("Sucursal Centro", ana, "9999", null);
        Assert.False(mal.Exito);
        Assert.Equal("PIN_INCORRECTO", mal.Codigo);

        await e.Kiosco.FicharAsync("Sucursal Centro", ana, "9999", null);
        var tercero = await e.Kiosco.FicharAsync("Sucursal Centro", ana, "9999", null);
        Assert.Equal("BLOQUEADO", tercero.Codigo);

        // Ya bloqueada: ni con el PIN correcto
        var ahoraBien = await e.Kiosco.FicharAsync("Sucursal Centro", ana, Entorno.PinAna, null);
        Assert.Equal("BLOQUEADO", ahoraBien.Codigo);
        Assert.Empty(e.StorageFichajes.Guardados);
    }

    [Fact]
    public async Task ConFotoObligatoria_SinFotoNoFicha_ConFotoSeGuarda()
    {
        var e = new Entorno();
        await e.ConfigFichaje.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = "Libre", Foto = "Obligatoria" });
        var ana = IdDe(e, "ana@test.com");

        var sin = await e.Kiosco.FicharAsync("Sucursal Centro", ana, Entorno.PinAna, null);
        Assert.Equal("FOTO_REQUERIDA", sin.Codigo);

        var con = await e.Kiosco.FicharAsync("Sucursal Centro", ana, Entorno.PinAna, Imagenes.Jpeg);
        Assert.True(con.Exito);
        Assert.Equal(Imagenes.Jpeg, e.Fotos.Datos[con.Fichaje!.Foto!]);
    }

    [Fact]
    public async Task LaCuentaDeLaSucursal_NoPuedeFicharPorSiMisma()
    {
        var e = new Entorno();
        var sucursal = e.AgregarSucursal();

        var r = await e.Kiosco.FicharAsync("Sucursal Centro", sucursal.Id, Entorno.PinKiosco, null);

        Assert.False(r.Exito);
        Assert.Equal("SIN_PERMISO", r.Codigo);
    }
}

public class KioscoApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;

    public KioscoApiTests(ApiFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Datos.AgregarSucursal();
        _fabrica.Datos.ConfigFichajeRepo.Datos = new ConfiguracionFichaje { Modo = ConfiguracionFichaje.ModoKiosco };
    }

    private async Task<(HttpClient Cliente, string Token)> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (cliente, token);
    }

    [Fact]
    public async Task SoloLaCuentaDeLaSucursalUsaElKiosco_YSuSesionDuraUnMes()
    {
        var (centro, tokenCentro) = await ClienteDe("centro@test.com", Entorno.PinKiosco);
        var (ana, tokenAna) = await ClienteDe("ana@test.com", Entorno.PinAna);

        Assert.Equal(HttpStatusCode.OK, (await centro.GetAsync("/api/kiosco/empleados")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/kiosco/empleados")).StatusCode);

        // La cuenta de la sucursal tampoco ficha como persona
        Assert.Equal(HttpStatusCode.Forbidden, (await centro.GetAsync("/api/fichaje/estado")).StatusCode);

        var manejador = new JwtSecurityTokenHandler();
        Assert.True(manejador.ReadJwtToken(tokenCentro).ValidTo > DateTime.UtcNow.AddDays(29));
        Assert.True(manejador.ReadJwtToken(tokenAna).ValidTo < DateTime.UtcNow.AddHours(13));
    }

    [Fact]
    public async Task FicharEnKiosco_ElPinIncorrectoEs400_NoUn401QueCierreLaSesionDelTelefono()
    {
        var (centro, _) = await ClienteDe("centro@test.com", Entorno.PinKiosco);
        var ana = _fabrica.Datos.Empleados.Datos.First(x => x.Email == "ana@test.com").Id;

        var mal = await centro.PostAsJsonAsync("/api/kiosco/fichaje", new { empleadoId = ana, pin = "0000" });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        Assert.Equal("PIN_INCORRECTO", (await mal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());

        var bien = await centro.PostAsJsonAsync("/api/kiosco/fichaje", new { empleadoId = ana, pin = Entorno.PinAna });
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
        var json = await bien.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Entrada", json.GetProperty("tipo").GetString());
        Assert.Contains("Ana Empleada", json.GetProperty("mensaje").GetString());
    }
}
