using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class LoginTests
{
    [Fact]
    public async Task LoginCorrecto_DevuelveRolesYPermisos()
    {
        var e = new Entorno();

        var r = await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna);

        Assert.Equal(LoginResultado.Exito, r.Resultado);
        Assert.Equal(new[] { "Empleado" }, r.Roles);
        Assert.Contains(CatalogoPermisos.FicharAsistencia, r.Permisos);
        Assert.DoesNotContain(CatalogoPermisos.LiquidarHoras, r.Permisos);
    }

    [Fact]
    public async Task ElRolInicialDeLaColumnaF_DaLosPermisosDelRol()
    {
        var e = new Entorno();

        var admin = await e.Login.AutenticarAsync("admin@test.com", Entorno.PinAdmin);
        var supervisor = await e.Login.AutenticarAsync("beto@test.com", Entorno.PinBeto);

        Assert.Equal(CatalogoPermisos.Todos.Length, admin.Permisos.Count);
        Assert.Contains(CatalogoPermisos.VerPanelSupervisor, supervisor.Permisos);
        Assert.DoesNotContain(CatalogoPermisos.AdministrarUsuarios, supervisor.Permisos);
    }

    [Fact]
    public async Task TresPinIncorrectos_BloqueanAlUsuario_AunConElPinCorrecto()
    {
        var e = new Entorno();

        Assert.Equal(LoginResultado.CredencialesInvalidas, (await e.Login.AutenticarAsync("ana@test.com", "0000")).Resultado);
        Assert.Equal(LoginResultado.CredencialesInvalidas, (await e.Login.AutenticarAsync("ana@test.com", "0000")).Resultado);
        Assert.Equal(LoginResultado.UsuarioBloqueado, (await e.Login.AutenticarAsync("ana@test.com", "0000")).Resultado);

        Assert.Equal(LoginResultado.UsuarioBloqueado, (await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna)).Resultado);
        Assert.True(e.Empleados.Datos.Single(x => x.Id == 2).Bloqueado);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion == "USUARIO_BLOQUEADO");
        Assert.Contains(e.HistorialRepo.Datos, h => h.Tipo == "BLOQUEO" && h.EmpleadoId == 2);
    }

    [Fact]
    public async Task UnLoginCorrecto_ReiniciaLosIntentos()
    {
        var e = new Entorno();

        await e.Login.AutenticarAsync("ana@test.com", "0000");
        await e.Login.AutenticarAsync("ana@test.com", "0000");
        await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna);

        Assert.Equal(0, e.Empleados.Datos.Single(x => x.Id == 2).IntentosFallidos);
        Assert.Equal(LoginResultado.CredencialesInvalidas, (await e.Login.AutenticarAsync("ana@test.com", "0000")).Resultado);
    }

    [Fact]
    public async Task UsuarioDadoDeBaja_NoEntra_NiCuentaIntentos()
    {
        var e = new Entorno();
        e.Empleados.Datos.Single(x => x.Id == 2).Activo = false;

        var r = await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna);

        Assert.Equal(LoginResultado.CredencialesInvalidas, r.Resultado);
        Assert.Equal(0, e.Empleados.Datos.Single(x => x.Id == 2).IntentosFallidos);
    }

    [Fact]
    public async Task EmailInexistente_DaElMismoErrorQueUnPinMalo()
    {
        var e = new Entorno();
        Assert.Equal(LoginResultado.CredencialesInvalidas, (await e.Login.AutenticarAsync("nadie@test.com", "1234")).Resultado);
    }

    [Fact]
    public async Task ElAdministradorDesbloquea_YElUsuarioVuelveAEntrar()
    {
        var e = new Entorno();
        for (int i = 0; i < 3; i++) await e.Login.AutenticarAsync("ana@test.com", "0000");

        await e.Usuarios.DesbloquearAsync("admin@test.com", 2);

        Assert.Equal(LoginResultado.Exito, (await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna)).Resultado);
    }
}

public class UsuariosAdminTests
{
    [Fact]
    public async Task NoSePuedeDarDeBajaAlUnicoAdministrador()
    {
        var e = new Entorno();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.DarDeBajaAsync("admin@test.com", 1));

        Assert.Contains("quedaría sin ningún usuario activo", ex.Message);
        Assert.True(e.Empleados.Datos.Single(x => x.Id == 1).Activo);
    }

    [Fact]
    public async Task ConOtroAdministrador_SePuedeDarDeBaja_YQuedaHistorial()
    {
        var e = new Entorno();
        await e.Usuarios.AsignarRolesAsync("admin@test.com", 3, new List<int> { e.IdRol("Administrador") });

        await e.Usuarios.DarDeBajaAsync("admin@test.com", 1);

        Assert.False(e.Empleados.Datos.Single(x => x.Id == 1).Activo);
        var baja = Assert.Single(e.HistorialRepo.Datos, h => h.Tipo == "BAJA");
        Assert.True(baja.Activo); // la foto guarda el último estado activo
    }

    [Fact]
    public async Task NoSePuedeSacarElRolAlUnicoAdministrador()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Usuarios.AsignarRolesAsync("admin@test.com", 1, new List<int> { e.IdRol("Empleado") }));
    }

    [Fact]
    public async Task UnUsuarioBloqueadoNoCuentaComoAdministradorActivo()
    {
        var e = new Entorno();
        await e.Usuarios.AsignarRolesAsync("admin@test.com", 3, new List<int> { e.IdRol("Administrador") });
        e.Empleados.Datos.Single(x => x.Id == 3).Bloqueado = true; // el segundo admin está bloqueado

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.DarDeBajaAsync("admin@test.com", 1));
    }

    [Fact]
    public async Task CrearUsuario_ValidaPinNombreYEmailUnicos()
    {
        var e = new Entorno();
        var roles = new List<int> { e.IdRol("Empleado") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CrearAsync("a", "Nuevo", "nuevo@test.com", "12", roles));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CrearAsync("a", "Nuevo", "nuevo@test.com", "abcd", roles));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CrearAsync("a", "ana empleada", "otro@test.com", "1234", roles));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CrearAsync("a", "Nuevo", "ANA@test.com", "1234", roles));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CrearAsync("a", "Nuevo", "nuevo@test.com", "1234", new List<int>()));
    }

    [Fact]
    public async Task CrearUsuario_GuardaElPinHasheado_Asigna_Y_Registra()
    {
        var e = new Entorno();

        int id = await e.Usuarios.CrearAsync("admin@test.com", "Carla Nueva", "carla@test.com", "4321", new List<int> { e.IdRol("Empleado") });

        var creado = e.Empleados.Datos.Single(x => x.Id == id);
        Assert.NotEqual("4321", creado.PinHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("4321", creado.PinHash));
        Assert.Equal(LoginResultado.Exito, (await e.Login.AutenticarAsync("carla@test.com", "4321")).Resultado);
        Assert.Contains(e.HistorialRepo.Datos, h => h.Tipo == "ALTA" && h.EmpleadoId == id);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion == "USUARIO_CREADO:Carla Nueva");
    }

    [Fact]
    public async Task NoSePuedeRenombrarAQuienYaTieneFichajes()
    {
        var e = new Entorno();
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new DateTime(2026, 10, 5, 8, 0, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Usuarios.ActualizarDatosAsync("admin@test.com", 2, "Ana Perez", "ana@test.com"));

        // el email sí se puede cambiar
        await e.Usuarios.ActualizarDatosAsync("admin@test.com", 2, "Ana Empleada", "ana.nueva@test.com");
        Assert.Equal("ana.nueva@test.com", e.Empleados.Datos.Single(x => x.Id == 2).Email);
    }

    [Fact]
    public async Task CambiarPinPropio_ExigeElPinActual()
    {
        var e = new Entorno();

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.CambiarPinPropioAsync(2, "9999", "5555"));
        await e.Usuarios.CambiarPinPropioAsync(2, Entorno.PinAna, "5555");

        Assert.Equal(LoginResultado.Exito, (await e.Login.AutenticarAsync("ana@test.com", "5555")).Resultado);
    }

    [Fact]
    public async Task Rollback_RestauraUnaFotoAnterior_YNoSePuedeEncadenar()
    {
        var e = new Entorno();
        await e.Usuarios.AsignarRolesAsync("admin@test.com", 2, new List<int> { e.IdRol("Supervisor") });
        var antes = e.HistorialRepo.Datos.Last(); // foto con Ana como Supervisor

        await e.Usuarios.AsignarRolesAsync("admin@test.com", 2, new List<int> { e.IdRol("Empleado") });
        await e.Usuarios.RollbackAsync("admin@test.com", antes.Id);

        Assert.Equal(new List<int> { e.IdRol("Supervisor") }, e.Roles.Asignaciones[2]);
        var rollback = e.HistorialRepo.Datos.Last();
        Assert.Equal("ROLLBACK", rollback.Tipo);
        Assert.Equal(antes.Id, rollback.VersionOrigen);

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Usuarios.RollbackAsync("admin@test.com", rollback.Id));
    }
}

public class RolesServiceTests
{
    [Fact]
    public async Task ElRolProtegidoNoSeElimina()
    {
        var e = new Entorno();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.EliminarAsync("a", e.IdRol("Administrador")));
        Assert.Contains("del sistema", ex.Message);
    }

    [Fact]
    public async Task NoSeEliminaUnRolConUsuarios_NiUnPadreCuyoHijoLosTiene()
    {
        var e = new Entorno();
        int padre = await e.RolesServicio.AgregarAsync("a", "Gerencia", null);
        int hijo = await e.RolesServicio.AgregarAsync("a", "Sucursal", padre);
        await e.Usuarios.AsignarRolesAsync("a", 2, new List<int> { hijo });

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.EliminarAsync("a", hijo));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.EliminarAsync("a", padre));
    }

    [Fact]
    public async Task EliminarUnRolSinUsuarios_BorraSuSubarbolYSusPermisos()
    {
        var e = new Entorno();
        int padre = await e.RolesServicio.AgregarAsync("a", "Gerencia", null);
        int hijo = await e.RolesServicio.AgregarAsync("a", "Sucursal", padre);
        await e.RolesServicio.ActualizarPermisosAsync("a", hijo, new List<int> { e.Roles.Permisos[0].Id });

        await e.RolesServicio.EliminarAsync("a", padre);

        Assert.DoesNotContain(e.Roles.Roles, r => r.Id == padre || r.Id == hijo);
        Assert.DoesNotContain(e.Roles.RolPermisos, rp => rp.RolId == hijo);
    }

    [Fact]
    public async Task NoSePuedeCrearUnCiclo_NiMoverAUnAncestro()
    {
        var e = new Entorno();
        int a = await e.RolesServicio.AgregarAsync("x", "A", null);
        int b = await e.RolesServicio.AgregarAsync("x", "B", a);
        int c = await e.RolesServicio.AgregarAsync("x", "C", b);

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.CambiarPadreAsync("x", a, c));  // ciclo
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.CambiarPadreAsync("x", c, a));  // ancestro
    }

    [Fact]
    public async Task NombresDeRolDuplicadosOVacios_SeRechazan()
    {
        var e = new Entorno();
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.AgregarAsync("x", "empleado", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RolesServicio.AgregarAsync("x", "   ", null));
    }

    [Fact]
    public async Task DuplicarUnRol_CopiaSusPermisos_YNoQuedaProtegido()
    {
        var e = new Entorno();

        int copia = await e.RolesServicio.DuplicarAsync("x", e.IdRol("Administrador"), "Admin 2");

        Assert.False(e.Roles.Roles.Single(r => r.Id == copia).Protegido);
        Assert.Equal(CatalogoPermisos.Todos.Length, e.Roles.RolPermisos.Count(rp => rp.RolId == copia));
    }

    [Fact]
    public async Task QuitarLosPermisosDeAdministracionAlUnicoRolAdministrador_Falla()
    {
        var e = new Entorno();
        var sinAdministrar = e.Roles.Permisos.Where(p => p.Nombre == CatalogoPermisos.FicharAsistencia).Select(p => p.Id).ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.RolesServicio.ActualizarPermisosAsync("x", e.IdRol("Administrador"), sinAdministrar));
    }

    [Fact]
    public async Task NoHayHerencia_ElPadreNoLeDaSusPermisosAlHijo()
    {
        var e = new Entorno();
        int hijo = await e.RolesServicio.AgregarAsync("x", "Hijo de admin", e.IdRol("Administrador"));
        await e.Usuarios.AsignarRolesAsync("x", 2, new List<int> { hijo });

        var r = await e.Login.AutenticarAsync("ana@test.com", Entorno.PinAna);

        Assert.Empty(r.Permisos); // el rol hijo no tiene permisos propios
    }

    [Fact]
    public async Task ElArbolSeArmaConLosPermisosDeCadaRol()
    {
        var e = new Entorno();
        var arbol = await e.RolesServicio.ObtenerArbolDtoAsync();

        var admin = arbol.Single(n => n.Nombre == "Administrador");
        Assert.True(admin.Protegido);
        Assert.Equal(CatalogoPermisos.Todos.Length, admin.Hijos.Count(h => !h.EsRol));
    }
}

public class FichajesYLiquidacionTests
{
    private static Entorno ConFichajes()
    {
        var e = new Entorno();
        e.Reloj.Ahora = new DateTime(2026, 11, 2, 12, 0, 0);

        // Ana: lunes 8 h, martes 10 h, domingo 4 h
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 5, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 5, 16, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 6, 8, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 6, 18, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 11, 9, 0, 0));
        e.Fichajes.Agregar("Ana Empleada", "Salida", new(2026, 10, 11, 13, 0, 0));
        // Beto: turno nocturno lunes 22:00 → martes 06:00
        e.Fichajes.Agregar("Beto Supervisor", "Entrada", new(2026, 10, 5, 22, 0, 0));
        e.Fichajes.Agregar("Beto Supervisor", "Salida", new(2026, 10, 6, 6, 0, 0));

        e.LiquidacionRepo.Valores.Add(new ValorHoraEmpleado { EmpleadoId = 2, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) });
        e.LiquidacionRepo.Valores.Add(new ValorHoraEmpleado { EmpleadoId = 3, Valor = 2000m, VigenteDesde = new DateTime(2026, 1, 1) });
        return e;
    }

    private static readonly DateTime Desde = new(2026, 10, 5);
    private static readonly DateTime Hasta = new(2026, 10, 11);

    [Fact]
    public async Task Calcular_UsaLasJornadasArmadasDeLaHoja()
    {
        var e = ConFichajes();

        var dias = await e.Liquidacion.CalcularAsync(Desde, Hasta);
        var resumen = LiquidacionService.Resumir(dias);

        var ana = resumen.Single(r => r.Empleado == "Ana Empleada");
        Assert.Equal(8 * 60 + 10 * 60 + 4 * 60, ana.MinTrabajados);
        // 8 h normales + (8 normales + 2 extra al 50 %) + 4 h de domingo al 100 %
        Assert.Equal(1000m * (480 + 480 + 120 * 1.5m + 240 * 2m) / 60m, ana.Importe);

        var beto = resumen.Single(r => r.Empleado == "Beto Supervisor");
        Assert.Equal(16000m, beto.Importe); // 8 h nocturnas a 2000, en el día en que empezó
    }

    [Fact]
    public async Task Cerrar_ConUnFichajeIncompleto_SeRechaza()
    {
        var e = ConFichajes();
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 7, 8, 0, 0)); // sin salida

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.CerrarAsync("admin@test.com", Desde, Hasta));

        Assert.Contains("incompleto", ex.Message);
        Assert.Empty(e.LiquidacionRepo.Periodos);
    }

    [Fact]
    public async Task CorregirUnaSalidaFaltante_PermiteCerrar()
    {
        var e = ConFichajes();
        e.Fichajes.Agregar("Ana Empleada", "Entrada", new(2026, 10, 7, 8, 0, 0));
        var incompletos = await e.FichajesAdmin.ListarIncompletosAsync(Desde, Hasta, soloVencidos: false);
        var abierto = Assert.Single(incompletos);
        Assert.Equal("SinSalida", abierto.Tipo);

        await e.FichajesAdmin.CorregirAsync("admin@test.com", abierto.EntradaId,
            new DateTime(2026, 10, 7, 8, 0, 0), new DateTime(2026, 10, 7, 16, 0, 0), "Olvidó fichar la salida");

        Assert.Empty(await e.FichajesAdmin.ListarIncompletosAsync(Desde, Hasta, false));
        var periodo = await e.Liquidacion.CerrarAsync("admin@test.com", Desde, Hasta);
        Assert.Equal("Cerrada", periodo.Estado);
        Assert.Contains(e.BitacoraRepo.Datos, b => b.Accion == "FICHAJE_CORREGIDO:Ana Empleada");
    }

    [Fact]
    public async Task Corregir_ExigeMotivo_OrdenValido_Y_NoSuperponer()
    {
        var e = ConFichajes();
        int entrada = e.Fichajes.Datos.First(f => f.NombreCompleto == "Ana Empleada").Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.CorregirAsync("a", entrada,
            new DateTime(2026, 10, 5, 8, 0, 0), new DateTime(2026, 10, 5, 16, 0, 0), "  "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.CorregirAsync("a", entrada,
            new DateTime(2026, 10, 5, 16, 0, 0), new DateTime(2026, 10, 5, 8, 0, 0), "al revés"));

        // Ana ya trabajó el martes 8-18: extender el lunes hasta el martes a las 9 se superpone
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.CorregirAsync("a", entrada,
            new DateTime(2026, 10, 5, 8, 0, 0), new DateTime(2026, 10, 6, 9, 0, 0), "se superpone"));
    }

    [Fact]
    public async Task Corregir_DejaLaMarcaDeCorreccionEnElCalculo()
    {
        var e = ConFichajes();
        int entrada = e.Fichajes.Datos.First(f => f.NombreCompleto == "Ana Empleada").Id;

        await e.FichajesAdmin.CorregirAsync("a", entrada, new(2026, 10, 5, 9, 0, 0), new(2026, 10, 5, 16, 0, 0), "Llegó tarde");

        var dia = (await e.Liquidacion.CalcularAsync(Desde, Hasta, "Ana Empleada")).First(d => d.Fecha == Desde);
        Assert.Equal(7 * 60, dia.MinPagables);
        Assert.Contains("Fichaje corregido", dia.Observacion);
    }

    [Fact]
    public async Task Cerrar_NoPermiteElPeriodoEnCurso_NiSuperponerse_NiSinValorHora()
    {
        var e = ConFichajes();

        // período que termina hoy o después
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.CerrarAsync("a", Desde, new DateTime(2026, 11, 2)));

        // sin valor hora de Beto
        e.LiquidacionRepo.Valores.RemoveAll(v => v.EmpleadoId == 3);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.CerrarAsync("a", Desde, Hasta));
        Assert.Contains("Beto Supervisor", ex.Message);

        // con todo cargado se cierra, y un segundo cierre que se superpone falla
        e.LiquidacionRepo.Valores.Add(new ValorHoraEmpleado { EmpleadoId = 3, Valor = 2000m, VigenteDesde = new DateTime(2026, 1, 1) });
        await e.Liquidacion.CerrarAsync("a", Desde, Hasta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.CerrarAsync("a", Hasta, new DateTime(2026, 10, 18)));
    }

    [Fact]
    public async Task UnPeriodoCerrado_NoPermiteCorregirNiAgregarNiAprobar()
    {
        var e = ConFichajes();
        await e.Liquidacion.CerrarAsync("a", Desde, Hasta);
        int entrada = e.Fichajes.Datos.First(f => f.NombreCompleto == "Ana Empleada").Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.CorregirAsync("a", entrada,
            new(2026, 10, 5, 9, 0, 0), new(2026, 10, 5, 16, 0, 0), "tarde"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.AgregarManualAsync("a", "Ana Empleada",
            new(2026, 10, 8, 8, 0, 0), new(2026, 10, 8, 16, 0, 0), "olvidado"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.AprobarExcedenteAsync("a", "Ana Empleada", new DateTime(2026, 10, 6)));
    }

    [Fact]
    public async Task LaLiquidacionCerrada_EsUnaFotoQueNoCambiaAlSubirElValorHora()
    {
        var e = ConFichajes();
        var periodo = await e.Liquidacion.CerrarAsync("a", Desde, Hasta);
        var antes = LiquidacionService.Resumir(await e.Liquidacion.ObtenerDiasDeLiquidacionAsync(periodo.Id)).Sum(r => r.Importe);

        e.LiquidacionRepo.Valores.Add(new ValorHoraEmpleado { EmpleadoId = 2, Valor = 5000m, VigenteDesde = new DateTime(2026, 10, 1) });

        var despues = LiquidacionService.Resumir(await e.Liquidacion.ObtenerDiasDeLiquidacionAsync(periodo.Id)).Sum(r => r.Importe);
        Assert.Equal(antes, despues);
    }

    [Fact]
    public async Task MarcarPagada_SoloUnaVez_YSoloSiEstaCerrada()
    {
        var e = ConFichajes();
        var periodo = await e.Liquidacion.CerrarAsync("a", Desde, Hasta);

        await e.Liquidacion.MarcarPagadaAsync("a", periodo.Id);

        Assert.Equal("Pagada", e.LiquidacionRepo.Periodos.Single().Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.MarcarPagadaAsync("a", periodo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.MarcarPagadaAsync("a", 999));
    }

    [Fact]
    public async Task AgregarManual_CargaEntradaYSalida_ConMotivo()
    {
        var e = ConFichajes();

        await e.FichajesAdmin.AgregarManualAsync("admin@test.com", "Ana Empleada",
            new(2026, 10, 8, 8, 0, 0), new(2026, 10, 8, 16, 0, 0), "Se cortó el celular");

        var dia = (await e.Liquidacion.CalcularAsync(Desde, Hasta, "Ana Empleada")).Single(d => d.Fecha == new DateTime(2026, 10, 8));
        Assert.Equal(480, dia.MinPagables);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.AgregarManualAsync("a", "Nadie",
            new(2026, 10, 9, 8, 0, 0), new(2026, 10, 9, 16, 0, 0), "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.FichajesAdmin.AgregarManualAsync("a", "Ana Empleada",
            new(2026, 12, 9, 8, 0, 0), new(2026, 12, 9, 16, 0, 0), "futuro"));
    }

    [Fact]
    public async Task Parametros_SeValidan()
    {
        var e = ConFichajes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarParametrosAsync("a", new ParametrosLiquidacion { ToleranciaMinutos = 99 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarParametrosAsync("a", new ParametrosLiquidacion { RecargoExtraPct = 500 }));
        await e.Liquidacion.GuardarParametrosAsync("a", new ParametrosLiquidacion { RedondeoMinutos = 0 });
        Assert.Equal(0, (await e.Liquidacion.ObtenerParametrosAsync()).RedondeoMinutos);
    }

    [Fact]
    public async Task ValorHora_DebeSerPositivo_YDelEmpleadoExistente()
    {
        var e = ConFichajes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarValorHoraAsync("a", 2, 0m, DateTime.Today));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.GuardarValorHoraAsync("a", 99, 100m, DateTime.Today));
    }

    [Fact]
    public async Task ElRangoDeLiquidacion_NoPuedeSuperarLosDiasMaximos()
    {
        var e = ConFichajes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Liquidacion.CalcularAsync(new DateTime(2026, 1, 1), new DateTime(2026, 6, 1)));
    }
}

public class BitacoraTests
{
    [Fact]
    public async Task Listar_FiltraPorUsuarioAccionYFechas_ConPaginacion()
    {
        var e = new Entorno();
        e.Reloj.Ahora = new DateTime(2026, 10, 1, 9, 0, 0);
        await e.Bitacora.RegistrarAsync("ana@test.com", "LOGIN");
        e.Reloj.Ahora = new DateTime(2026, 10, 2, 9, 0, 0);
        await e.Bitacora.RegistrarAsync("admin@test.com", "ROL_CREADO:Gerencia");
        await e.Bitacora.RegistrarAsync("admin@test.com", "ROL_ELIMINADO:Gerencia");

        Assert.Equal(3, (await e.Bitacora.ListarAsync(null, null, null, null, 1, 50)).Total);
        Assert.Equal(2, (await e.Bitacora.ListarAsync("admin@test.com", null, null, null, 1, 50)).Total);
        Assert.Equal(1, (await e.Bitacora.ListarAsync(null, "creado", null, null, 1, 50)).Total);
        Assert.Equal(1, (await e.Bitacora.ListarAsync(null, null, null, new DateTime(2026, 10, 1), 1, 50)).Total);

        var pagina = await e.Bitacora.ListarAsync(null, null, null, null, 2, 2);
        Assert.Single(pagina.Items);
        Assert.Equal(3, pagina.Total);
    }

    [Fact]
    public async Task LasAccionesLargas_SeTruncan()
    {
        var e = new Entorno();
        await e.Bitacora.RegistrarAsync("x", new string('a', 500));
        Assert.Equal(100, e.BitacoraRepo.Datos.Single().Accion.Length);
    }
}
