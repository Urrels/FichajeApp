using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Seguridad;

namespace FichajeApp.Api.Tests;

// A-01: nadie puede dar, quitar ni armar un rol con permisos que él mismo no tiene (si no, quien solo administra usuarios se haría Administrador).
public class EscaladaDeRolesTests
{
    private const string Admin = "admin@test.com", Gestor = "beto@test.com";

    // Beto pasa a ser "Gestor de usuarios": puede administrar usuarios, fichar y cambiar su PIN, y nada más
    private static async Task<(Entorno Entorno, int RolGestor)> ConGestorDeUsuarios()
    {
        var e = new Entorno();
        int rol = await e.RolesServicio.AgregarAsync(Admin, "Gestor de usuarios", null);
        var ids = e.Roles.Permisos.Where(p => p.Nombre is CatalogoPermisos.AdministrarUsuarios or CatalogoPermisos.FicharAsistencia or CatalogoPermisos.CambiarPin)
            .Select(p => p.Id).ToList();
        await e.RolesServicio.ActualizarPermisosAsync(Admin, rol, ids);
        await e.Usuarios.AsignarRolesAsync(Admin, 3, new List<int> { rol });
        return (e, rol);
    }

    [Fact]
    public async Task Quien_solo_administra_usuarios_no_puede_asignarse_el_rol_Administrador()
    {
        var (e, gestor) = await ConGestorDeUsuarios();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Usuarios.AsignarRolesAsync(Gestor, 3, new List<int> { gestor, e.IdRol("Administrador") }));

        Assert.Contains("Administrador", ex.Message);
        Assert.Equal(new[] { gestor }, e.Roles.Asignaciones[3]);   // nada cambió
    }

    [Fact]
    public async Task Tampoco_puede_crear_un_usuario_con_el_rol_Administrador_pero_si_con_roles_dentro_de_sus_permisos()
    {
        var (e, _) = await ConGestorDeUsuarios();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Usuarios.CrearAsync(Gestor, "Intruso", "intruso@test.com", "5827", new List<int> { e.IdRol("Administrador") }));
        Assert.DoesNotContain(e.Empleados.Datos, p => p.Email == "intruso@test.com");

        var id = await e.Usuarios.CrearAsync(Gestor, "Nuevo", "nuevo@test.com", "5827", new List<int> { e.IdRol("Empleado") });
        Assert.Equal(new[] { e.IdRol("Empleado") }, e.Roles.Asignaciones[id]);
    }

    [Fact]
    public async Task Tampoco_puede_quitarle_el_rol_Administrador_a_otro()
    {
        var (e, _) = await ConGestorDeUsuarios();
        int otroAdmin = await e.Usuarios.CrearAsync(Admin, "Otro admin", "otro@test.com", "5827", new List<int> { e.IdRol("Administrador") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Usuarios.AsignarRolesAsync(Gestor, otroAdmin, new List<int> { e.IdRol("Empleado") }));

        Assert.Contains("No podés dar ni quitar", ex.Message);
        Assert.Contains(e.IdRol("Administrador"), e.Roles.Asignaciones[otroAdmin]);
    }

    [Fact]
    public async Task Editar_a_alguien_sin_tocar_sus_roles_altos_no_se_bloquea()
    {
        var (e, _) = await ConGestorDeUsuarios();

        // El usuario 1 es Administrador y el gestor le "asigna" lo mismo que ya tiene: no cambia nada, no hay nada que impedir
        await e.Usuarios.AsignarRolesAsync(Gestor, 1, new List<int> { e.IdRol("Administrador") });

        Assert.Equal(new[] { e.IdRol("Administrador") }, e.Roles.Asignaciones[1]);
    }

    [Fact]
    public async Task El_Administrador_puede_asignar_cualquier_rol()
    {
        var e = new Entorno();

        await e.Usuarios.AsignarRolesAsync(Admin, 2, new List<int> { e.IdRol("Supervisor") });
        await e.Usuarios.AsignarRolesAsync(Admin, 2, new List<int> { e.IdRol("Administrador") });

        Assert.Equal(new[] { e.IdRol("Administrador") }, e.Roles.Asignaciones[2]);
    }

    [Fact]
    public async Task Restaurar_una_version_vieja_no_sirve_para_darse_un_rol_que_no_se_podia_dar()
    {
        var (e, _) = await ConGestorDeUsuarios();
        await e.Usuarios.AsignarRolesAsync(Admin, 2, new List<int> { e.IdRol("Administrador") });
        var conAdministrador = (await e.Usuarios.HistorialAsync(2)).First(h => h.RolIds.Contains(e.IdRol("Administrador")));
        await e.Usuarios.AsignarRolesAsync(Admin, 2, new List<int> { e.IdRol("Empleado") });

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.RollbackAsync(Gestor, conAdministrador.Id));

        Assert.Equal(new[] { e.IdRol("Empleado") }, e.Roles.Asignaciones[2]);
    }

    [Fact]
    public async Task No_se_le_suman_a_un_rol_permisos_que_quien_lo_edita_no_tiene()
    {
        var (e, gestor) = await ConGestorDeUsuarios();
        var liquidar = e.Roles.Permisos.First(p => p.Nombre == CatalogoPermisos.LiquidarHoras).Id;
        var cambiarPin = e.Roles.Permisos.First(p => p.Nombre == CatalogoPermisos.CambiarPin).Id;
        var fichar = e.Roles.Permisos.First(p => p.Nombre == CatalogoPermisos.FicharAsistencia).Id;
        var administrar = e.Roles.Permisos.First(p => p.Nombre == CatalogoPermisos.AdministrarUsuarios).Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RolesServicio.ActualizarPermisosAsync(Gestor, gestor, new List<int> { administrar, fichar, cambiarPin, liquidar }));
        Assert.Contains(CatalogoPermisos.LiquidarHoras, ex.Message);

        // Quitar permisos, o dejar los que ya tiene, sí se puede
        await e.RolesServicio.ActualizarPermisosAsync(Gestor, gestor, new List<int> { administrar, fichar });
        Assert.DoesNotContain(e.Roles.RolPermisos, rp => rp.RolId == gestor && rp.PermisoId == cambiarPin);
    }
}

// A-02: los permisos viajan dentro del token; si la persona los perdió, el token deja de valer enseguida (no a las 12 h ni a los 30 días).
public class PermisosDelTokenTests
{
    private static async Task<HttpClient> ClienteDe(ApiFactory fabrica, string email, string pin)
    {
        var cliente = fabrica.CreateClient();
        var respuesta = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task Quien_deja_de_ser_Supervisor_pierde_el_acceso_con_el_token_que_ya_tenia()
    {
        var fabrica = new ApiFactory();
        var admin = await ClienteDe(fabrica, "admin@test.com", Entorno.PinAdmin);
        var beto = await ClienteDe(fabrica, "beto@test.com", Entorno.PinBeto);
        Assert.Equal(HttpStatusCode.OK, (await beto.GetAsync("/api/supervisor/presentes")).StatusCode);

        var empleado = fabrica.Datos.IdRol("Empleado");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/usuarios/3/roles", new { rolIds = new[] { empleado } })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await beto.GetAsync("/api/supervisor/presentes")).StatusCode);
    }

    [Fact]
    public async Task Si_se_le_quita_un_permiso_al_rol_cae_el_token_de_todos_los_que_lo_tienen()
    {
        var fabrica = new ApiFactory();
        var admin = await ClienteDe(fabrica, "admin@test.com", Entorno.PinAdmin);
        var beto = await ClienteDe(fabrica, "beto@test.com", Entorno.PinBeto);
        Assert.Equal(HttpStatusCode.OK, (await beto.GetAsync("/api/supervisor/presentes")).StatusCode);

        var permisos = fabrica.Datos.Roles.Permisos;
        var sinPanel = permisos.Where(p => p.Nombre is CatalogoPermisos.FicharAsistencia or CatalogoPermisos.CambiarPin or CatalogoPermisos.CorregirFichajes)
            .Select(p => p.Id).ToArray();
        var r = await admin.PutAsJsonAsync($"/api/admin/roles/{fabrica.Datos.IdRol("Supervisor")}/permisos", new { permisoIds = sinPanel });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await beto.GetAsync("/api/supervisor/presentes")).StatusCode);
    }

    [Fact]
    public async Task Ganar_permisos_no_corta_la_sesion_y_la_baja_la_corta_enseguida()
    {
        var fabrica = new ApiFactory();
        var admin = await ClienteDe(fabrica, "admin@test.com", Entorno.PinAdmin);
        var ana = await ClienteDe(fabrica, "ana@test.com", Entorno.PinAna);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/fichaje/estado")).StatusCode);

        await admin.PutAsJsonAsync("/api/admin/usuarios/2/roles", new { rolIds = new[] { fabrica.Datos.IdRol("Supervisor") } });
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/fichaje/estado")).StatusCode);   // el token viejo sigue sirviendo para lo que ya podía

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/usuarios/2/baja", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/api/fichaje/estado")).StatusCode);
    }

    [Fact]
    public async Task Por_la_API_quien_solo_administra_usuarios_no_se_hace_Administrador()
    {
        var fabrica = new ApiFactory();
        var datos = fabrica.Datos;
        int rol = await datos.RolesServicio.AgregarAsync("admin@test.com", "Gestor de usuarios", null);
        var ids = datos.Roles.Permisos.Where(p => p.Nombre is CatalogoPermisos.AdministrarUsuarios or CatalogoPermisos.FicharAsistencia or CatalogoPermisos.CambiarPin)
            .Select(p => p.Id).ToList();
        await datos.RolesServicio.ActualizarPermisosAsync("admin@test.com", rol, ids);
        await datos.Usuarios.AsignarRolesAsync("admin@test.com", 3, new List<int> { rol });
        var gestor = await ClienteDe(fabrica, "beto@test.com", Entorno.PinBeto);

        var r = await gestor.PutAsJsonAsync("/api/admin/usuarios/3/roles", new { rolIds = new[] { rol, datos.IdRol("Administrador") } });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Administrador", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
        Assert.Equal(new[] { rol }, datos.Roles.Asignaciones[3]);
    }
}
