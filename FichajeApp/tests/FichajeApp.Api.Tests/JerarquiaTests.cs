using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;
using Microsoft.Extensions.Logging.Abstractions;

namespace FichajeApp.Api.Tests;

// Jerarquía: Administrador (1) > Supervisor "Beto" (3) > Empleada "Ana" (2)
public class JerarquiaTests
{
    private readonly Entorno _e = new();
    private readonly NotificacionesRepoFalso _repo = new();
    private readonly DispositivosFalso _dispositivos = new();
    private readonly EnviadorPushFalso _enviador = new();
    private readonly AvisosPorJerarquia _avisos;
    private readonly AvisoFichajeASupervisores _observer;
    private readonly AlertasDeFichaje _alertas;

    public JerarquiaTests()
    {
        var push = new PushService(_dispositivos, _enviador, _e.Reloj);
        var notificaciones = new NotificacionesService(_repo, _e.Reloj, push: push);
        _avisos = new AvisosPorJerarquia(_e.Jerarquia, notificaciones);
        _observer = new AvisoFichajeASupervisores(_e.Empleados, _avisos, NullLogger<AvisoFichajeASupervisores>.Instance);
        _alertas = new AlertasDeFichaje(_avisos, _e.Empleados, _e.Jornadas, _e.LiquidacionRepo, _e.Reloj);
        // Todos tienen el teléfono registrado: así se ve a quién le sonó
        foreach (var id in new[] { 1, 2, 3 }) push.RegistrarAsync(id, $"token-{id}").Wait();
    }

    private async Task AsignarAnaABeto() => await _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 2, 3);

    private static FichajeDto Entrada(DateTime cuando) =>
        new() { NombreCompleto = "Ana Empleada", TipoFichaje = "Entrada", FechaHora = cuando, Lugar = "Sede" };

    private List<string> Sonaron => _enviador.Enviados.Select(x => x.Token).OrderBy(t => t).ToList();
    private List<int> ConRegistro => _repo.Datos.Select(n => n.EmpleadoId).OrderBy(i => i).ToList();

    // ---------- Quién ve a quién ----------

    [Fact]
    public async Task ElAdministrador_VeATodos()
    {
        var alcance = await _e.Jerarquia.AlcanceDeAsync("admin@test.com");
        Assert.True(alcance.EsGlobal);
        Assert.True(alcance.Incluye("Ana Empleada"));
    }

    [Fact]
    public async Task UnSupervisorSinEmpleados_SoloSeVeASiMismo()
    {
        var alcance = await _e.Jerarquia.AlcanceDeAsync("beto@test.com");
        Assert.False(alcance.EsGlobal);
        Assert.True(alcance.Incluye("Beto Supervisor"));
        Assert.False(alcance.Incluye("Ana Empleada"));
    }

    [Fact]
    public async Task UnSupervisor_VeSoloASusEmpleados()
    {
        await AsignarAnaABeto();
        _e.Empleados.Datos.Add(new EmpleadoDto { Id = 4, NombreCompleto = "Carla Otra", Email = "carla@test.com", PinHash = "x", Activo = true });

        var alcance = await _e.Jerarquia.AlcanceDeAsync("beto@test.com");
        Assert.True(alcance.Incluye("Ana Empleada"));
        Assert.False(alcance.Incluye("Carla Otra"));
        Assert.False(alcance.Incluye("Admin Prueba"));
    }

    // ---------- Asignar ----------

    [Fact]
    public async Task SeAsignaUnSupervisor_YQuedaEnLaListaDeUsuarios()
    {
        await AsignarAnaABeto();

        var lista = await _e.Usuarios.ListarAsync(null, 1, 50);
        var ana = lista.Items.Single(u => u.Id == 2);
        Assert.Equal(3, ana.SupervisorId);
        Assert.Equal("Beto Supervisor", ana.Supervisor);
    }

    [Fact]
    public async Task SePuedeQuitarElSupervisor()
    {
        await AsignarAnaABeto();
        await _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 2, null);
        Assert.Null((await _e.Empleados.ObtenerPorIdAsync(2))!.SupervisorId);
    }

    [Fact]
    public async Task NoSePuedeAsignarComoSupervisorAUnEmpleadoComun()
    {
        _e.Empleados.Datos.Add(new EmpleadoDto { Id = 4, NombreCompleto = "Carla Otra", Email = "carla@test.com", PinHash = "x", Activo = true });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 4, 2));
        Assert.Contains("rol de supervisor", ex.Message);
    }

    [Fact]
    public async Task NadieEsSuPropioSupervisor_NiElAdministradorTieneSupervisor()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 3, 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 1, 3));
    }

    [Fact]
    public async Task UnSupervisorNoPuedeDependerDeOtroSupervisor()
    {
        _e.Empleados.Datos.Add(new EmpleadoDto { Id = 4, NombreCompleto = "Sofi Supervisora", Email = "sofi@test.com", PinHash = "x", Activo = true, Rol = "Supervisor" });
        // Beto ya tiene a Ana a cargo: no puede pasar a depender de Sofi
        await AsignarAnaABeto();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 3, 4));
        Assert.Contains("a cargo", ex.Message);
    }

    [Fact]
    public async Task UnSupervisorDadoDeBaja_NoSePuedeAsignar()
    {
        _e.Empleados.Datos.First(p => p.Id == 3).Activo = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _e.Usuarios.AsignarSupervisorAsync("admin@test.com", 2, 3));
    }

    // ---------- Avisos por jerarquía ----------

    [Fact]
    public async Task ElAvisoSuenaSoloEnElTelefonoDelSupervisor_YElAdminSoloRecibeElRegistro()
    {
        await AsignarAnaABeto();
        _repo.Datos.Clear();
        _enviador.Enviados.Clear();

        await _observer.AvisarAsync(Entrada(new DateTime(2026, 11, 10, 8, 5, 0)));

        Assert.Equal(new[] { "token-3" }, Sonaron);          // push: solo Beto
        Assert.Equal(new[] { 1, 3 }, ConRegistro);           // centro de notificaciones: Beto y el administrador (auditoría)
        Assert.All(_repo.Datos, n => Assert.DoesNotContain("Sin supervisor", n.Mensaje));
    }

    [Fact]
    public async Task SinSupervisorAsignado_ElAvisoVaDirectoAlAdministradorConPush()
    {
        await _observer.AvisarAsync(Entrada(new DateTime(2026, 11, 10, 8, 5, 0)));

        Assert.Equal(new[] { "token-1" }, Sonaron);
        Assert.Equal(new[] { 1 }, ConRegistro);
        Assert.Contains("Sin supervisor asignado", _repo.Datos[0].Mensaje);
    }

    [Fact]
    public async Task SiElSupervisorSeDaDeBaja_LosAvisosPasanAlAdministrador()
    {
        await AsignarAnaABeto();
        _e.Empleados.Datos.First(p => p.Id == 3).Activo = false;
        _repo.Datos.Clear();
        _enviador.Enviados.Clear();

        await _observer.AvisarAsync(Entrada(DateTime.Now));

        Assert.Equal(new[] { "token-1" }, Sonaron);
        Assert.Contains("Sin supervisor asignado", _repo.Datos[0].Mensaje);
    }

    [Fact]
    public async Task SiElSupervisorPierdeElPermiso_LosAvisosPasanAlAdministrador()
    {
        await AsignarAnaABeto();
        // Beto pasa a ser un empleado común (rol "Empleado")
        var rolEmpleado = _e.IdRol(FichajeApp.Api.Dominio.Seguridad.CatalogoPermisos.RolEmpleado);
        await _e.Roles.ReemplazarAsignacionesAsync(3, new[] { rolEmpleado });
        _repo.Datos.Clear();
        _enviador.Enviados.Clear();

        await _observer.AvisarAsync(Entrada(DateTime.Now));

        Assert.Equal(new[] { "token-1" }, Sonaron);
    }

    [Fact]
    public async Task SiElSupervisorFicha_ElAvisoVaAlAdministrador_YNoSeAvisaASiMismo()
    {
        await _observer.AvisarAsync(new FichajeDto { NombreCompleto = "Beto Supervisor", TipoFichaje = "Salida", FechaHora = DateTime.Now });

        Assert.Equal(new[] { "token-1" }, Sonaron);
        Assert.DoesNotContain(_repo.Datos, n => n.EmpleadoId == 3);
    }

    [Fact]
    public async Task ElAdministradorQueFicha_NoSeAvisaASiMismo()
    {
        await _observer.AvisarAsync(new FichajeDto { NombreCompleto = "Admin Prueba", TipoFichaje = "Entrada", FechaHora = DateTime.Now });
        Assert.Empty(_repo.Datos);   // no hay otro administrador a quien avisar
    }

    // ---------- Alertas: huella, cara y fichaje abierto ----------

    [Fact]
    public async Task UnaHuellaQueFalla_LeLlegaAlSupervisor()
    {
        await AsignarAnaABeto();
        _repo.Datos.Clear();
        _enviador.Enviados.Clear();
        var ana = (await _e.Empleados.ObtenerPorIdAsync(2))!;

        Assert.True(await _alertas.ReportarAsync(ana, "biometria"));

        Assert.Equal(new[] { "token-3" }, Sonaron);
        Assert.Equal("No pasó la huella", _repo.Datos.First(n => n.EmpleadoId == 3).Titulo);
    }

    [Fact]
    public async Task UnaAlertaRepetida_NoSeVuelveAEnviarDeInmediato()
    {
        var ana = (await _e.Empleados.ObtenerPorIdAsync(2))!;
        Assert.True(await _alertas.ReportarAsync(ana, "cara"));
        Assert.False(await _alertas.ReportarAsync(ana, "cara"));       // mismo tipo, mismo rato
        Assert.True(await _alertas.ReportarAsync(ana, "biometria"));   // otro tipo sí
    }

    [Fact]
    public async Task UnTipoDeAlertaInventado_SeIgnora()
    {
        var ana = (await _e.Empleados.ObtenerPorIdAsync(2))!;
        Assert.False(await _alertas.ReportarAsync(ana, "hackeo"));
        Assert.Empty(_repo.Datos);
    }

    [Fact]
    public async Task UnFichajeAbiertoDemasiadoTiempo_AvisaUnaSolaVez()
    {
        await AsignarAnaABeto();
        _repo.Datos.Clear();
        _enviador.Enviados.Clear();
        // Entrada de hace 3 días con el límite de 16 h (valor por defecto)
        _e.Fichajes.Agregar("Ana Empleada", "Entrada", _e.Reloj.Ahora.AddDays(-1).AddHours(-1));

        Assert.Equal(1, await _alertas.RevisarFichajesAbiertosAsync());
        Assert.Equal(0, await _alertas.RevisarFichajesAbiertosAsync());   // ya se avisó de esa jornada

        Assert.Equal(new[] { "token-3" }, Sonaron);
        Assert.Contains("sin salida", _repo.Datos.First(n => n.EmpleadoId == 3).Mensaje);
    }
}
