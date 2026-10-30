using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// El kiosco NO viene activado: lo prende el administrador (Cómo se ficha → Modo) y recién ahí aparece el rol de sucursal.
public class ModoKioscoTests
{
    private static bool HayRolKiosco(Entorno e) => e.Roles.Roles.Any(r => r.Nombre == CatalogoPermisos.RolKiosco);

    private static Task Guardar(Entorno e, string? modo, string ubicacion = "Libre", string foto = "No") =>
        e.ConfigFichaje.GuardarAsync("admin@test.com", new GuardarConfiguracionFichajeRequest { Ubicacion = ubicacion, Foto = foto, Modo = modo });

    [Fact]
    public async Task DeEntrada_EsModoPersonal_YNoExisteElRolKiosco()
    {
        var e = new Entorno();

        Assert.Equal(ConfiguracionFichaje.ModoPersonal, (await e.ConfigFichaje.ObtenerAsync()).Modo);
        Assert.False(HayRolKiosco(e));
        Assert.DoesNotContain(CatalogoPermisos.RolKiosco, CatalogoPermisos.RolesIniciales.Keys);
    }

    [Fact]
    public async Task AlActivarElKiosco_SeCreaElRolConSuPermiso_UnaSolaVez()
    {
        var e = new Entorno();

        await Guardar(e, "Kiosco");
        await Guardar(e, "Kiosco"); // guardar de nuevo no duplica nada

        var roles = e.Roles.Roles.Where(r => r.Nombre == CatalogoPermisos.RolKiosco).ToList();
        Assert.Single(roles);
        Assert.False(roles[0].Protegido);

        var permisoId = e.Roles.Permisos.First(p => p.Nombre == CatalogoPermisos.FicharEnKiosco).Id;
        Assert.Contains(e.Roles.RolPermisos, rp => rp.RolId == roles[0].Id && rp.PermisoId == permisoId);

        Assert.Single(e.BitacoraRepo.Datos, b => b.Accion == "ROL_KIOSCO_CREADO");
        Assert.Equal(ConfiguracionFichaje.ModoKiosco, (await e.ConfigFichaje.ObtenerAsync()).Modo);
    }

    [Fact]
    public async Task GuardarSinIndicarElModo_ConservaElQueHabia()
    {
        var e = new Entorno();
        await Guardar(e, "Kiosco");

        await Guardar(e, modo: null, ubicacion: "Ninguna", foto: "Obligatoria"); // solo cambia lo otro

        var c = await e.ConfigFichaje.ObtenerAsync();
        Assert.Equal(ConfiguracionFichaje.ModoKiosco, c.Modo);
        Assert.Equal("Ninguna", c.Ubicacion);
        Assert.Equal("Obligatoria", c.Foto);
    }

    [Fact]
    public async Task VolverAPersonal_ApagaElModo_PeroNoBorraElRol()
    {
        var e = new Entorno();
        await Guardar(e, "Kiosco");

        await Guardar(e, "Personal");

        Assert.Equal(ConfiguracionFichaje.ModoPersonal, (await e.ConfigFichaje.ObtenerAsync()).Modo);
        Assert.True(HayRolKiosco(e)); // las cuentas de sucursal ya creadas siguen existiendo
    }

    [Fact]
    public async Task UnModoInvalido_SeRechaza_YNoCambiaNada()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Guardar(e, "Mixto"));

        Assert.Null(e.ConfigFichajeRepo.Datos);
        Assert.False(HayRolKiosco(e));
    }

    [Fact]
    public async Task ModoPersonalNoCreaElRol()
    {
        var e = new Entorno();

        await Guardar(e, "Personal");

        Assert.False(HayRolKiosco(e));
    }

    [Fact]
    public async Task SiElRolYaExiste_NoSeDuplica()
    {
        var e = new Entorno();
        Assert.True(await RolKiosco.AsegurarAsync(e.Roles));
        Assert.False(await RolKiosco.AsegurarAsync(e.Roles));

        Assert.Single(e.Roles.Roles, r => r.Nombre == CatalogoPermisos.RolKiosco);
    }

    [Fact]
    public async Task SinElCatalogoSembrado_NoCreaUnRolSinPermisos()
    {
        var vacio = new RolesFalso(); // planilla nueva sin permisos todavía

        Assert.False(await RolKiosco.AsegurarAsync(vacio));
        Assert.Empty(vacio.Roles);
    }

    [Fact]
    public async Task LaCuentaDeSucursalCreadaPorElModo_EsSoloKiosco()
    {
        var e = new Entorno();
        await Guardar(e, "Kiosco");
        var rol = e.Roles.Roles.First(r => r.Nombre == CatalogoPermisos.RolKiosco);
        var permisos = (await e.Roles.ListarPermisosAsync()).Where(p => e.Roles.RolPermisos.Any(rp => rp.RolId == rol.Id && rp.PermisoId == p.Id))
            .Select(p => p.Nombre).ToList();

        Assert.True(CatalogoPermisos.EsSoloKiosco(permisos));
    }
}

// Por HTTP: con el modo en Personal el kiosco está apagado; el administrador lo prende y recién ahí funciona
public class ActivarElKioscoPorHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public ActivarElKioscoPorHttpTests(ApiFactory fabrica) => _fabrica = fabrica;

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
    public async Task ElKioscoEstaApagadoHastaQueElAdministradorLoActiva()
    {
        // Una sola prueba con todo el recorrido: comparten la configuración, así que el orden importa
        _fabrica.Datos.AgregarSucursal(); // la cuenta ya creada (el rol existe porque alguien activó el kiosco antes)
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        var sucursal = await ClienteDe("centro@test.com", Entorno.PinKiosco);

        // Apagado (por defecto): avisa con un 409 y un mensaje, que la app muestra tal cual
        var apagado = await sucursal.GetAsync("/api/kiosco/empleados");
        Assert.Equal(HttpStatusCode.Conflict, apagado.StatusCode);
        var cuerpo = await apagado.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KIOSCO_DESACTIVADO", cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("Cómo se ficha", cuerpo.GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await sucursal.PostAsJsonAsync("/api/kiosco/fichaje", new { empleadoId = 2, pin = "2222" })).StatusCode);

        // Un empleado no puede activarlo
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", modo = "Kiosco" })).StatusCode);

        // El administrador lo ve en Personal y lo activa
        var antes = await (await admin.GetAsync("/api/admin/configuracion-fichaje")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Personal", antes.GetProperty("modo").GetString());

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", modo = "Kiosco" })).StatusCode);

        // Ahora sí funciona
        var activo = await sucursal.GetAsync("/api/kiosco/empleados");
        Assert.Equal(HttpStatusCode.OK, activo.StatusCode);
        var fichar = await sucursal.PostAsJsonAsync("/api/kiosco/fichaje", new { empleadoId = 2, pin = Entorno.PinAna });
        Assert.Equal(HttpStatusCode.OK, fichar.StatusCode);

        // Y se puede volver a apagar
        await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", modo = "Personal" });
        Assert.Equal(HttpStatusCode.Conflict, (await sucursal.GetAsync("/api/kiosco/empleados")).StatusCode);
    }

    [Fact]
    public async Task UnModoInvalidoPorHttp_Es400()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.PutAsJsonAsync("/api/admin/configuracion-fichaje", new { ubicacion = "Libre", foto = "No", modo = "Mixto" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
