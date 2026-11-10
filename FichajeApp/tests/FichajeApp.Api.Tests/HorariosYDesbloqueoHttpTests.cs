using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FichajeApp.Api.Tests;

public class HorariosEnMemoria : IHorariosRepositorio
{
    public List<HorarioEmpleado> Datos { get; } = new();

    public Task<List<HorarioEmpleado>> ListarAsync() => Task.FromResult(Datos.ToList());

    public Task GuardarAsync(int empleadoId, IReadOnlyCollection<HorarioEmpleado> horarios)
    {
        Datos.RemoveAll(h => h.EmpleadoId == empleadoId);
        Datos.AddRange(horarios);
        return Task.CompletedTask;
    }
}

// La API completa en memoria, pero con los horarios guardados en una lista (la base SQL real no está en los tests)
public class ApiFactoryConHorarios : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(servicios =>
        {
            servicios.RemoveAll<IHorariosRepositorio>();
            servicios.AddSingleton<IHorariosRepositorio>(new HorariosEnMemoria());
        });
    }
}

// Estos tests existen porque una vez se "agregaron" los endpoints de horario y nunca llegaron al controlador:
// los tests de servicios pasaban y la app habría recibido un 404.
public class HorariosHttpTests : IClassFixture<ApiFactoryConHorarios>
{
    private readonly ApiFactoryConHorarios _fabrica;

    public HorariosHttpTests(ApiFactoryConHorarios fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    private static object Dia(int dia, string entrada, string salida) => new { dia, entrada, salida };

    [Fact]
    public async Task ElAdministrador_GuardaYLeeElHorarioDeUnEmpleado()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var vacio = await admin.GetFromJsonAsync<JsonElement>("/api/admin/liquidacion/horario/2");
        Assert.Equal(0, vacio.GetArrayLength());

        var guardar = await admin.PutAsJsonAsync("/api/admin/liquidacion/horario/2", new[] { Dia(1, "08:00", "16:00"), Dia(2, "22:00", "06:00") });
        Assert.Equal(HttpStatusCode.OK, guardar.StatusCode);

        var leido = await admin.GetFromJsonAsync<JsonElement>("/api/admin/liquidacion/horario/2");
        Assert.Equal(2, leido.GetArrayLength());
        Assert.Equal("08:00", leido[0].GetProperty("entrada").GetString());
        Assert.Equal("06:00", leido[1].GetProperty("salida").GetString());
    }

    [Theory]
    [InlineData("08:00", "08:00")]    // misma hora
    [InlineData("06:00", "23:00")]    // más de 16 horas
    [InlineData("8am", "16:00")]      // hora mal escrita
    public async Task ElHorarioInvalido_SeRechazaCon400(string entrada, string salida)
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PutAsJsonAsync("/api/admin/liquidacion/horario/2", new[] { Dia(3, entrada, salida) });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task UnEmpleado_NoPuedeVerNiCambiarHorarios()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/admin/liquidacion/horario/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/admin/liquidacion/horario/2", new[] { Dia(1, "08:00", "16:00") })).StatusCode);
    }
}

// El bloqueo temporal por intentos fallidos vive en memoria: desbloquear o restablecer el PIN desde la administración
// tiene que levantarlo también, no solo limpiar la base.
public class DesbloqueoHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;

    public DesbloqueoHttpTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpResponseMessage> Login(string email, string pin) =>
        await _fabrica.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, pin });

    [Fact]
    public async Task DesbloquearORestablecerElPin_LevantaElBloqueoTemporalAlInstante()
    {
        var r = await Login("admin@test.com", Entorno.PinAdmin);
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var admin = _fabrica.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Beto se bloquea y el administrador lo desbloquea
        for (int i = 0; i < 3; i++) await Login("beto@test.com", "0000");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("beto@test.com", Entorno.PinBeto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/usuarios/3/desbloquear", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login("beto@test.com", Entorno.PinBeto)).StatusCode);

        // Ana se bloquea y el administrador le restablece el PIN (sin repetir el chequeo de bloqueo: el limite de login por minuto es 10)
        for (int i = 0; i < 3; i++) await Login("ana@test.com", "0000");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/usuarios/2/restablecer-pin", new { pin = "4815" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login("ana@test.com", "4815")).StatusCode);
    }
}
