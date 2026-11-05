using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Persistencia.Sql;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Tests;

public class SitiosAdminFalso : ISitiosAdminRepositorio
{
    public List<SitioAdmin> Datos { get; } = new();

    public Task<List<SitioAdmin>> ListarAsync() => Task.FromResult(Datos.OrderBy(s => s.Nombre).ToList());

    public Task<int> CrearAsync(Sitio s)
    {
        var id = Datos.Count == 0 ? 1 : Datos.Max(x => x.Id) + 1;
        Datos.Add(new SitioAdmin(id, s.Nombre, s.Latitud, s.Longitud, s.RadioMetros));
        return Task.FromResult(id);
    }

    public Task ActualizarAsync(int id, Sitio s)
    {
        var i = Datos.FindIndex(x => x.Id == id);
        if (i < 0) throw new InvalidOperationException("El lugar no existe.");
        Datos[i] = new SitioAdmin(id, s.Nombre, s.Latitud, s.Longitud, s.RadioMetros);
        return Task.CompletedTask;
    }

    public Task EliminarAsync(int id)
    {
        if (Datos.RemoveAll(x => x.Id == id) == 0) throw new InvalidOperationException("El lugar no existe.");
        return Task.CompletedTask;
    }
}

public class SitiosAdminServiceTests
{
    private static GuardarSitioRequest Valido(string nombre = "Sede central") => new(nombre, -34.6037, -58.3816, 100);

    private static (SitiosAdminService Servicio, Entorno Entorno, IMemoryCache Cache) Armar()
    {
        var entorno = new Entorno();
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new SitiosAdminService(entorno.SitiosAdmin, entorno.Bitacora, cache), entorno, cache);
    }

    [Fact]
    public async Task Crear_guarda_el_lugar_recortado_y_deja_constancia_en_la_bitacora()
    {
        var (servicio, entorno, _) = Armar();

        var id = await servicio.CrearAsync("admin@test.com", Valido("  Sede central  "));

        var lugar = Assert.Single(await servicio.ListarAsync());
        Assert.Equal((id, "Sede central", 100.0), (lugar.Id, lugar.Nombre, lugar.RadioMetros));
        Assert.Contains(entorno.BitacoraRepo.Datos, e => e.Accion == "SITIO_CREADO:Sede central" && e.Usuario == "admin@test.com");
    }

    [Theory]
    [InlineData("", -34.6, -58.4, 100, "nombre")]
    [InlineData("   ", -34.6, -58.4, 100, "nombre")]
    [InlineData("X", 91, -58.4, 100, "ubicación")]
    [InlineData("X", -34.6, 181, 100, "ubicación")]
    [InlineData("X", 0, 0, 100, "ubicación")]
    [InlineData("X", -34.6, -58.4, 5, "radio")]
    [InlineData("X", -34.6, -58.4, 6000, "radio")]
    public async Task Rechaza_datos_invalidos_con_un_mensaje_claro(string nombre, double lat, double lon, double radio, string palabra)
    {
        var (servicio, _, _) = Armar();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearAsync("a", new GuardarSitioRequest(nombre, lat, lon, radio)));

        Assert.Contains(palabra, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await servicio.ListarAsync());
    }

    [Fact]
    public async Task Rechaza_un_nombre_muy_largo_y_uno_repetido_pero_deja_editar_el_mismo_lugar()
    {
        var (servicio, _, _) = Armar();
        var id = await servicio.CrearAsync("a", Valido("Sede"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearAsync("a", Valido(new string('x', 101))));
        var repetido = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearAsync("a", Valido("SEDE")));
        Assert.Contains("ese nombre", repetido.Message);

        await servicio.ActualizarAsync("a", id, new GuardarSitioRequest("sede", -34.7, -58.5, 250));   // el mismo lugar, con otra capitalización
        Assert.Equal(250, (await servicio.ListarAsync()).Single().RadioMetros);
    }

    [Fact]
    public async Task Eliminar_borra_el_lugar_y_uno_inexistente_falla()
    {
        var (servicio, entorno, _) = Armar();
        var id = await servicio.CrearAsync("a", Valido());

        await servicio.EliminarAsync("a", id);

        Assert.Empty(await servicio.ListarAsync());
        Assert.Contains(entorno.BitacoraRepo.Datos, e => e.Accion == "SITIO_ELIMINADO:Sede central");
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EliminarAsync("a", id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarAsync("a", 99, Valido()));
    }

    [Fact]
    public async Task Un_cambio_invalida_la_lista_cacheada_que_usa_el_fichaje()
    {
        var (servicio, entorno, cache) = Armar();
        var cacheado = new CachedSitiosRepository(new SitiosDesdeAdmin(entorno.SitiosAdmin), cache, Microsoft.Extensions.Options.Options.Create(new FichajeOptions { CacheMinutos = 10 }));

        Assert.Empty(await cacheado.ObtenerSitiosAsync());
        await servicio.CrearAsync("a", Valido());

        Assert.Equal("Sede central", Assert.Single(await cacheado.ObtenerSitiosAsync()).Nombre);
    }

    private sealed class SitiosDesdeAdmin : ISitiosRepository
    {
        private readonly ISitiosAdminRepositorio _admin;
        public SitiosDesdeAdmin(ISitiosAdminRepositorio admin) => _admin = admin;
        public async Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync() =>
            (await _admin.ListarAsync()).Select(s => new Sitio(s.Nombre, s.Latitud, s.Longitud, s.RadioMetros)).ToList();
    }
}

public class SitiosSqlTests
{
    [Fact]
    public async Task El_repositorio_SQL_crea_lista_actualiza_y_elimina()
    {
        using var bd = new BaseSqlite();
        var repo = new SitiosSqlRepositorio(bd);

        var b = await repo.CrearAsync(new Sitio("Bodega", -34.7, -58.5, 80));
        var a = await repo.CrearAsync(new Sitio("Almacén", -34.6, -58.4, 120));

        Assert.Equal(new[] { "Almacén", "Bodega" }, (await repo.ListarAsync()).Select(s => s.Nombre));
        Assert.Equal(new[] { "Bodega", "Almacén" }, (await repo.ObtenerSitiosAsync()).Select(s => s.Nombre));   // el fichaje los lee en el orden de carga

        await repo.ActualizarAsync(b, new Sitio("Bodega Norte", -34.8, -58.6, 90));
        Assert.Equal("Bodega Norte", (await repo.ListarAsync()).Single(s => s.Id == b).Nombre);

        await repo.EliminarAsync(a);
        Assert.Single(await repo.ListarAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EliminarAsync(a));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ActualizarAsync(99, new Sitio("X", 1, 1, 50)));
    }
}

public class SitiosAdminApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public SitiosAdminApiTests(ApiFactory fabrica) => _fabrica = fabrica;

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var respuesta = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task El_administrador_carga_lista_edita_y_borra_lugares()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var creado = await admin.PostAsJsonAsync("/api/admin/sitios", new { nombre = "Sede", latitud = -34.6, longitud = -58.4, radioMetros = 100 });
        Assert.Equal(HttpStatusCode.OK, creado.StatusCode);
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var lista = await admin.GetFromJsonAsync<JsonElement>("/api/admin/sitios");
        Assert.Equal("Sede", lista[0].GetProperty("nombre").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/sitios/{id}", new { nombre = "Sede 2", latitud = -34.6, longitud = -58.4, radioMetros = 150 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/admin/sitios/{id}")).StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/api/admin/sitios")).GetArrayLength());
    }

    [Fact]
    public async Task Un_dato_invalido_responde_400_con_el_mensaje()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PostAsJsonAsync("/api/admin/sitios", new { nombre = "", latitud = -34.6, longitud = -58.4, radioMetros = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("nombre", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Sin_el_permiso_de_configurar_fichaje_responde_403()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/admin/sitios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/admin/sitios", new { nombre = "X", latitud = 1.0, longitud = 1.0, radioMetros = 50 })).StatusCode);
    }
}
