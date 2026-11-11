using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FichajeApp.Api.Tests;

// Lo que importa de la jerarquía, probado por HTTP contra la API completa: un supervisor NO ve ni toca a quien no tiene a cargo.
// La API completa, pero con los fichajes guardados en memoria (la fábrica común los deja vacíos)
public class ApiFactoryConFichajes : ApiFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(servicios =>
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<FichajeApp.Api.Storage.IFichajeStorageStrategy>(servicios);
            servicios.AddSingleton<FichajeApp.Api.Storage.IFichajeStorageStrategy>(Datos.StorageFichajes);
        });
    }
}

public class JerarquiaHttpTests : IClassFixture<ApiFactoryConFichajes>
{
    private readonly ApiFactoryConFichajes _fabrica;

    public JerarquiaHttpTests(ApiFactoryConFichajes fabrica)
    {
        _fabrica = fabrica;
        // Ana (2) está a cargo de Beto (3); "Carla" no está a cargo de nadie
        var datos = _fabrica.Datos;
        datos.Empleados.Datos.First(p => p.Id == 2).SupervisorId = 3;
        if (datos.Empleados.Datos.All(p => p.Id != 4))
            datos.Empleados.Datos.Add(new EmpleadoDto { Id = 4, NombreCompleto = "Carla Otra", Email = "carla@test.com", PinHash = "x", Activo = true });

        if (datos.StorageFichajes.Guardados.Count == 0)
        {
            var ahora = DateTime.Now;
            datos.StorageFichajes.Guardados.Add(new FichajeDto { NombreCompleto = "Ana Empleada", TipoFichaje = "Entrada", FechaHora = ahora.Date.AddMinutes(1) });
            datos.StorageFichajes.Guardados.Add(new FichajeDto { NombreCompleto = "Carla Otra", TipoFichaje = "Entrada", FechaHora = ahora.Date.AddMinutes(2) });
        }
    }

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    private static List<string> Nombres(JsonElement lista, string campo = "nombreCompleto") =>
        lista.EnumerateArray().Select(x => x.GetProperty(campo).GetString()!).OrderBy(n => n).ToList();

    [Fact]
    public async Task ElSupervisor_VeSoloLosFichajesDeSusEmpleados_YElAdminTodos()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var deBeto = await beto.GetFromJsonAsync<JsonElement>("/api/supervisor/fichajes");
        Assert.Equal(new[] { "Ana Empleada" }, Nombres(deBeto));

        var deAdmin = await admin.GetFromJsonAsync<JsonElement>("/api/supervisor/fichajes");
        Assert.Equal(new[] { "Ana Empleada", "Carla Otra" }, Nombres(deAdmin));
    }

    [Fact]
    public async Task ElSupervisor_NoPuedePedirElHistorialDeAlguienQueNoTieneACargo()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);
        var r = await beto.GetAsync("/api/supervisor/fichajes?empleado=" + Uri.EscapeDataString("Carla Otra"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        var recorrido = await beto.GetAsync("/api/supervisor/recorrido?empleado=" + Uri.EscapeDataString("Carla Otra"));
        Assert.Equal(HttpStatusCode.Forbidden, recorrido.StatusCode);
    }

    [Fact]
    public async Task ElSupervisor_VeSoloSusPresentes_YSoloSusEmpleadosEnElFiltro()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);

        var presentes = await beto.GetFromJsonAsync<JsonElement>("/api/supervisor/presentes");
        Assert.Equal(new[] { "Ana Empleada" }, Nombres(presentes));

        var filtro = await beto.GetFromJsonAsync<JsonElement>("/api/supervisor/empleados");
        Assert.Equal(new[] { "Ana Empleada", "Beto Supervisor" }, filtro.EnumerateArray().Select(x => x.GetString()!).OrderBy(n => n).ToList());
    }

    [Fact]
    public async Task ElSupervisor_NoPuedeCorregirElFichajeDeAlguienQueNoTieneACargo()
    {
        // Un fichaje abierto de Carla (que no está a cargo de Beto)
        _fabrica.Datos.Fichajes.Agregar("Carla Otra", "Entrada", DateTime.Now.Date.AddDays(-1).AddHours(8));
        var id = _fabrica.Datos.Fichajes.Datos.Last().Id;
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ayer = DateTime.Now.Date.AddDays(-1);

        var cuerpo = new { entradaId = id, ingreso = ayer.AddHours(8), egreso = ayer.AddHours(16), motivo = "x" };
        Assert.Equal(HttpStatusCode.Forbidden, (await beto.PostAsJsonAsync("/api/admin/fichajes/corregir", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/fichajes/corregir", cuerpo)).StatusCode);
    }

    [Fact]
    public async Task ElSupervisor_NoPuedeCargarUnFichajeManualDeAlguienQueNoTieneACargo()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);
        var ayer = DateTime.Now.Date.AddDays(-2);
        var r = await beto.PostAsJsonAsync("/api/admin/fichajes/manual",
            new { empleado = "Carla Otra", ingreso = ayer.AddHours(8), egreso = ayer.AddHours(16), motivo = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task SoloElAdministrador_AsignaSupervisores()
    {
        var beto = await ClienteDe("beto@test.com", Entorno.PinBeto);
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        // Una persona aparte: la fábrica se comparte entre pruebas y Carla tiene que seguir sin supervisor
        if (_fabrica.Datos.Empleados.Datos.All(p => p.Id != 5))
            _fabrica.Datos.Empleados.Datos.Add(new EmpleadoDto { Id = 5, NombreCompleto = "Dani Nuevo", Email = "dani@test.com", PinHash = "x", Activo = true });

        Assert.Equal(HttpStatusCode.Forbidden, (await beto.PutAsJsonAsync("/api/admin/usuarios/5/supervisor", new { supervisorId = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/usuarios/5/supervisor", new { supervisorId = 3 })).StatusCode);

        var candidatos = await admin.GetFromJsonAsync<JsonElement>("/api/admin/usuarios/supervisores");
        Assert.Equal(new[] { "Beto Supervisor" }, Nombres(candidatos, "nombre"));
    }

    [Fact]
    public async Task UnaAlertaDeHuella_SoloLaPuedeEnviarQuienFicha_YElTipoSeValida()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/fichaje/alerta", new { tipo = "otra-cosa" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/fichaje/alerta", new { tipo = "biometria" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PostAsJsonAsync("/api/fichaje/alerta", new { tipo = "cara" })).StatusCode);
    }
}
