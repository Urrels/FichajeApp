using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Tests;

// Levanta la API real en memoria (DI, JWT, autorización por permiso, filtros, rate limiting) con los
// repositorios de Google Sheets reemplazados por los falsos: prueba el cableado de punta a punta por HTTP.
public class ApiFactory : WebApplicationFactory<Program>
{
    public Entorno Datos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(servicios =>
        {
            // Nada de Google en los tests
            servicios.RemoveAll<IHostedService>();
            servicios.RemoveAll<GoogleSheetsClient>();
            servicios.RemoveAll<TimeProvider>();
            servicios.RemoveAll<IEmpleadosRepositorio>();
            servicios.RemoveAll<IRolesRepositorio>();
            servicios.RemoveAll<IBitacoraRepositorio>();
            servicios.RemoveAll<IHistorialUsuariosRepositorio>();
            servicios.RemoveAll<IFichajesRepositorio>();
            servicios.RemoveAll<ILiquidacionRepositorio>();
            servicios.RemoveAll<IAparienciaRepositorio>();
            servicios.RemoveAll<IConfiguracionFichajeRepositorio>();
            servicios.RemoveAll<IAlmacenFotos>();
            servicios.RemoveAll<FichajeApp.Api.Services.ISitiosRepository>();
            servicios.RemoveAll<IFichajeStorageStrategy>();

            servicios.AddSingleton<TimeProvider>(Datos.Reloj);
            servicios.AddSingleton<IEmpleadosRepositorio>(Datos.Empleados);
            servicios.AddSingleton<IRolesRepositorio>(Datos.Roles);
            servicios.AddSingleton<IBitacoraRepositorio>(Datos.BitacoraRepo);
            servicios.AddSingleton<IHistorialUsuariosRepositorio>(Datos.HistorialRepo);
            servicios.AddSingleton<IFichajesRepositorio>(Datos.Fichajes);
            servicios.AddSingleton<ILiquidacionRepositorio>(Datos.LiquidacionRepo);
            servicios.AddSingleton<IAparienciaRepositorio>(Datos.AparienciaRepo);
            servicios.AddSingleton<IConfiguracionFichajeRepositorio>(Datos.ConfigFichajeRepo);
            servicios.AddSingleton<IAlmacenFotos>(Datos.Fotos);
            servicios.AddSingleton<FichajeApp.Api.Services.ISitiosRepository, SitiosVacio>();
            servicios.AddSingleton<IFichajeStorageStrategy, StorageVacio>();
        });
    }
}

public class SitiosVacio : FichajeApp.Api.Services.ISitiosRepository
{
    public Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync() => Task.FromResult<IReadOnlyList<Sitio>>(new List<Sitio>());
}

public class StorageVacio : IFichajeStorageStrategy
{
    public Task GuardarAsync(FichajeDto fichaje) => Task.CompletedTask;
    public Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto) => Task.FromResult("Salida");
    public Task<List<FichajeDto>> ObtenerFichajesAsync() => Task.FromResult(new List<FichajeDto>());
}

public class ApiIntegracionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;

    public ApiIntegracionTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var respuesta = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    private static async Task<string?> Mensaje(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString();

    [Fact]
    public async Task Login_DevuelveRolesYPermisos_EnLaRespuesta()
    {
        var r = await _fabrica.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "ana@test.com", pin = Entorno.PinAna });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Empleado", json.GetProperty("roles")[0].GetString());
        Assert.Contains(json.GetProperty("permisos").EnumerateArray(), p => p.GetString() == "Fichar asistencia");
    }

    [Fact]
    public async Task SinToken_401()
    {
        var r = await _fabrica.CreateClient().GetAsync("/api/admin/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task UnEmpleado_NoEntraAlaAdministracion_403()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        foreach (var ruta in new[] { "/api/admin/usuarios", "/api/admin/roles/arbol", "/api/admin/bitacora",
                                     "/api/admin/liquidacion/parametros", "/api/supervisor/presentes" })
            Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task ElSupervisor_VeSuPanel_PeroNoLaAdministracion()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);

        Assert.Equal(HttpStatusCode.OK, (await beto.GetAsync("/api/supervisor/presentes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await beto.GetAsync("/api/admin/usuarios")).StatusCode);
    }

    [Fact]
    public async Task ElAdministrador_ListaUsuariosYArbolDeRoles()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var usuarios = await (await admin.GetAsync("/api/admin/usuarios")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, usuarios.GetProperty("total").GetInt32());

        var arbol = await (await admin.GetAsync("/api/admin/roles/arbol")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(arbol.EnumerateArray(), n => n.GetProperty("nombre").GetString() == "Administrador");
    }

    [Fact]
    public async Task UnaReglaDeNegocio_SeDevuelveComo400ConMensaje()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PostAsJsonAsync("/api/admin/roles", new { nombre = "Empleado", padreId = (int?)null });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Ya existe un rol", await Mensaje(r));
    }

    [Fact]
    public async Task ElAdministrador_NoPuedeDarseDeBaja_SiEsElUnico()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PostAsync("/api/admin/usuarios/1/baja", null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("quedaría sin ningún usuario activo", await Mensaje(r));
    }

    [Fact]
    public async Task ElUsuarioDadoDeBaja_PierdeElAccesoAunqueSuTokenSigaVigente()
    {
        var fabrica = new ApiFactory();
        var admin = fabricaCliente(fabrica, await Token(fabrica, "admin@test.com", Entorno.PinAdmin));
        var ana = fabricaCliente(fabrica, await Token(fabrica, "ana@test.com", Entorno.PinAna));

        // Ana todavía no usó su token: el estado se consulta recién ahora
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/usuarios/2/baja", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/api/supervisor/presentes")).StatusCode);

        static HttpClient fabricaCliente(ApiFactory f, string token)
        {
            var c = f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return c;
        }
        static async Task<string> Token(ApiFactory f, string email, string pin)
        {
            var r = await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, pin });
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        }
    }

    [Fact]
    public async Task TresPinMalos_Bloquean_YElAdministradorDesbloquea()
    {
        var fabrica = new ApiFactory();
        var cliente = fabrica.CreateClient();

        for (int i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await cliente.PostAsJsonAsync("/api/auth/login", new { email = "ana@test.com", pin = "0000" })).StatusCode);

        var tercero = await cliente.PostAsJsonAsync("/api/auth/login", new { email = "ana@test.com", pin = "0000" });
        Assert.Contains("bloqueado", await Mensaje(tercero));

        var admin = fabrica.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.com", pin = Entorno.PinAdmin });
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/usuarios/2/desbloquear", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await cliente.PostAsJsonAsync("/api/auth/login", new { email = "ana@test.com", pin = Entorno.PinAna })).StatusCode);
    }

    [Fact]
    public async Task LaLiquidacion_SeCalculaCierraYExportaPorHttp()
    {
        var fabrica = new ApiFactory();
        var e = fabrica.Datos;
        e.Reloj.Ahora = new DateTime(2026, 11, 2, 12, 0, 0);
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 5, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 5, 16, 0, 0));
        e.LiquidacionRepo.Valores.Add(new ValorHoraEmpleado { EmpleadoId = 2, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) });

        var admin = fabrica.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.com", pin = Entorno.PinAdmin });
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());

        var calculo = await (await admin.GetAsync("/api/admin/liquidacion/calcular?desde=2026-10-05&hasta=2026-10-11"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8000m, calculo.GetProperty("resumen")[0].GetProperty("importe").GetDecimal());

        var cierre = await admin.PostAsJsonAsync("/api/admin/liquidacion/cerrar", new { desde = "2026-10-05", hasta = "2026-10-11" });
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);

        var otraVez = await admin.PostAsJsonAsync("/api/admin/liquidacion/cerrar", new { desde = "2026-10-05", hasta = "2026-10-11" });
        Assert.Equal(HttpStatusCode.BadRequest, otraVez.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/liquidacion/liquidaciones/1/pagar", null)).StatusCode);

        var csv = await admin.GetAsync("/api/admin/liquidacion/liquidaciones/1/csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var texto = await csv.Content.ReadAsStringAsync();
        Assert.Contains("Ana Empleada;8;8;8;0;0;0;8000,00", texto);
    }

    [Fact]
    public async Task ElLogin_TieneLimiteDeIntentosPorIp_429()
    {
        var fabrica = new ApiFactory();
        var cliente = fabrica.CreateClient();
        HttpResponseMessage? ultima = null;

        // usuario inexistente: no cuenta intentos de PIN, solo el límite de la ruta
        for (int i = 0; i < 12; i++)
            ultima = await cliente.PostAsJsonAsync("/api/auth/login", new { email = "nadie@test.com", pin = "0000" });

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima!.StatusCode);
        Assert.Contains("Demasiados intentos", await Mensaje(ultima));
    }
}
