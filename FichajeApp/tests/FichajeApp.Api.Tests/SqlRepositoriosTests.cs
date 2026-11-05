using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Persistencia;
using FichajeApp.Api.Persistencia.Sql;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Una base SQLite en memoria con las mismas tablas que SQL Server (se crean desde el modelo): prueban que cada repositorio
// SQL se comporta como el de la planilla. La conexión queda abierta mientras dure la prueba, si no la base desaparece.
public sealed class BaseSqlite : IDbContextFactory<FichajeDbContext>, IDisposable
{
    private readonly SqliteConnection _conexion = new("DataSource=:memory:");

    public BaseSqlite()
    {
        _conexion.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public FichajeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FichajeDbContext>().UseSqlite(_conexion).Options);

    public void Dispose() => _conexion.Dispose();
}

public class EmpleadosSqlTests
{
    private static EmpleadoDto Nuevo(string nombre, string email, bool activo = true) =>
        new() { NombreCompleto = nombre, Email = email, PinHash = "hash", Activo = activo };

    [Fact]
    public async Task Crear_asigna_ids_correlativos_y_listar_los_devuelve_en_orden()
    {
        using var bd = new BaseSqlite();
        var repo = new EmpleadosSqlRepositorio(bd);

        var ana = await repo.CrearAsync(Nuevo("Ana", "ana@x.com"));
        var beto = await repo.CrearAsync(Nuevo("Beto", "beto@x.com"));

        Assert.Equal(1, ana.Id);
        Assert.Equal(2, beto.Id);
        Assert.Equal(new[] { "Ana", "Beto" }, (await repo.ListarAsync()).Select(e => e.NombreCompleto));
    }

    [Fact]
    public async Task Buscar_por_email_ignora_mayusculas_y_excluye_inactivos_salvo_que_se_pida()
    {
        using var bd = new BaseSqlite();
        var repo = new EmpleadosSqlRepositorio(bd);
        await repo.CrearAsync(Nuevo("Ana", "Ana@X.com"));
        await repo.CrearAsync(Nuevo("Baja", "baja@x.com", activo: false));

        Assert.NotNull(await repo.BuscarPorEmailAsync("ana@x.COM"));
        Assert.Null(await repo.BuscarPorEmailAsync("baja@x.com"));
        Assert.NotNull(await repo.BuscarPorEmailAsync("baja@x.com", incluirInactivos: true));
        Assert.Null(await repo.BuscarPorEmailAsync("nadie@x.com"));
    }

    [Fact]
    public async Task Actualizar_guarda_los_cambios_y_un_empleado_inexistente_falla()
    {
        using var bd = new BaseSqlite();
        var repo = new EmpleadosSqlRepositorio(bd);
        var ana = await repo.CrearAsync(Nuevo("Ana", "ana@x.com"));

        ana.IntentosFallidos = 2;
        ana.Bloqueado = true;
        ana.Rol = "";
        await repo.ActualizarAsync(ana);

        var leida = (await repo.ObtenerPorIdAsync(ana.Id))!;
        Assert.Equal(2, leida.IntentosFallidos);
        Assert.True(leida.Bloqueado);
        Assert.Equal("Empleado", leida.Rol);   // un rol vacío se guarda como el rol por defecto

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ActualizarAsync(new EmpleadoDto { Id = 99, NombreCompleto = "X", Email = "x", PinHash = "h" }));
    }

    [Fact]
    public async Task No_se_puede_repetir_el_email_ni_el_nombre()
    {
        using var bd = new BaseSqlite();
        var repo = new EmpleadosSqlRepositorio(bd);
        await repo.CrearAsync(Nuevo("Ana", "ana@x.com"));

        await Assert.ThrowsAsync<DbUpdateException>(() => repo.CrearAsync(Nuevo("Otra", "ana@x.com")));
        await Assert.ThrowsAsync<DbUpdateException>(() => repo.CrearAsync(Nuevo("Ana", "otra@x.com")));
    }
}

public class RolesSqlTests
{
    [Fact]
    public async Task Permisos_de_un_rol_se_reemplazan_y_eliminar_el_rol_borra_sus_relaciones()
    {
        using var bd = new BaseSqlite();
        var repo = new RolesSqlRepositorio(bd);
        await repo.InsertarPermisoAsync("A");
        await repo.InsertarPermisoAsync("B");
        var rol = await repo.InsertarRolAsync("Gerente", null);
        var otro = await repo.InsertarRolAsync("Otro", rol);

        await repo.GuardarPermisosDeRolAsync(rol, new[] { 1, 2, 2 });
        await repo.GuardarPermisosDeRolAsync(rol, new[] { 2 });
        await repo.ReemplazarAsignacionesAsync(7, new[] { rol, otro });

        Assert.Equal(new[] { (rol, 2) }, await repo.ListarRolPermisosAsync());

        await repo.EliminarRolesAsync(new[] { rol });

        Assert.Equal(new[] { otro }, (await repo.ListarRolesAsync()).Select(r => r.Id));
        Assert.Empty(await repo.ListarRolPermisosAsync());
        Assert.Equal(new[] { otro }, (await repo.ListarAsignacionesAsync())[7]);
    }

    [Fact]
    public async Task Cambiar_el_padre_y_marcar_protegido_se_guardan_y_un_rol_inexistente_falla()
    {
        using var bd = new BaseSqlite();
        var repo = new RolesSqlRepositorio(bd);
        var admin = await repo.InsertarRolAsync("Administrador", null, protegido: true);
        var hijo = await repo.InsertarRolAsync("Hijo", null);

        await repo.CambiarPadreAsync(hijo, admin);

        var roles = await repo.ListarRolesAsync();
        Assert.True(roles.Single(r => r.Id == admin).Protegido);
        Assert.Equal(admin, roles.Single(r => r.Id == hijo).PadreId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CambiarPadreAsync(99, null));
    }

    [Fact]
    public async Task Reemplazar_asignaciones_deja_solo_las_nuevas_del_empleado()
    {
        using var bd = new BaseSqlite();
        var repo = new RolesSqlRepositorio(bd);
        await repo.ReemplazarAsignacionesAsync(1, new[] { 10, 11 });
        await repo.ReemplazarAsignacionesAsync(2, new[] { 10 });
        await repo.ReemplazarAsignacionesAsync(1, new[] { 12 });

        var asignaciones = await repo.ListarAsignacionesAsync();
        Assert.Equal(new[] { 12 }, asignaciones[1]);
        Assert.Equal(new[] { 10 }, asignaciones[2]);
    }
}

public class AuditoriaSqlTests
{
    [Fact]
    public async Task Bitacora_conserva_el_orden_en_que_se_registro()
    {
        using var bd = new BaseSqlite();
        var repo = new BitacoraSqlRepositorio(bd);
        await repo.RegistrarAsync(new EntradaBitacora { FechaHora = new(2026, 11, 2, 9, 0, 0), Usuario = "a@x.com", Accion = "LOGIN" });
        await repo.RegistrarAsync(new EntradaBitacora { FechaHora = new(2026, 11, 2, 9, 5, 0), Usuario = "b@x.com", Accion = "LOGIN_FALLIDO" });

        Assert.Equal(new[] { "LOGIN", "LOGIN_FALLIDO" }, (await repo.ListarAsync()).Select(b => b.Accion));
    }

    [Fact]
    public async Task Historial_guarda_los_roles_y_lista_lo_mas_nuevo_primero()
    {
        using var bd = new BaseSqlite();
        var repo = new HistorialUsuariosSqlRepositorio(bd);
        var id1 = await repo.RegistrarAsync(new HistorialUsuario { FechaHora = new(2026, 11, 1), EmpleadoId = 5, Tipo = "ALTA", RealizadoPor = "sistema", Nombre = "Ana", Email = "a@x.com", Activo = true, RolIds = new() { 3, 1 } });
        var id2 = await repo.RegistrarAsync(new HistorialUsuario { FechaHora = new(2026, 11, 2), EmpleadoId = 5, Tipo = "BAJA", RealizadoPor = "admin", Nombre = "Ana", Email = "a@x.com", VersionOrigen = id1 });
        await repo.RegistrarAsync(new HistorialUsuario { FechaHora = new(2026, 11, 3), EmpleadoId = 6, Tipo = "ALTA", RealizadoPor = "admin", Nombre = "Beto", Email = "b@x.com" });

        var deAna = await repo.ListarAsync(5);

        Assert.Equal(new[] { id2, id1 }, deAna.Select(h => h.Id));
        Assert.Equal(new[] { 1, 3 }, (await repo.ObtenerAsync(id1))!.RolIds);
        Assert.Equal(id1, deAna[0].VersionOrigen);
        Assert.Null(await repo.ObtenerAsync(99));
    }
}

public class FichajesSqlTests
{
    private static FichajeDto Fichaje(string nombre, string tipo, DateTime fecha) =>
        new() { NombreCompleto = nombre, FechaHora = fecha, TipoFichaje = tipo, Lugar = "Sede", Latitud = -34.6, Longitud = -58.4, Precision = 12.4 };

    [Fact]
    public async Task Ultimo_tipo_del_dia_es_el_del_ultimo_fichaje_de_hoy_y_sin_fichajes_el_proximo_es_entrada()
    {
        using var bd = new BaseSqlite();
        var guardar = new FichajesSqlStorage(bd);
        var hoy = DateTime.Now.Date;

        Assert.Equal("Salida", await guardar.ObtenerUltimoTipoFichajeAsync("Ana"));   // "Salida" = el próximo será Entrada

        await guardar.GuardarAsync(Fichaje("Ana", "Entrada", hoy.AddHours(8)));
        Assert.Equal("Entrada", await guardar.ObtenerUltimoTipoFichajeAsync("ana"));

        await guardar.GuardarAsync(Fichaje("Ana", "Salida", hoy.AddHours(12)));
        await guardar.GuardarAsync(Fichaje("Ana", "Entrada", hoy.AddDays(-1).AddHours(8)));   // de ayer: no cuenta
        Assert.Equal("Salida", await guardar.ObtenerUltimoTipoFichajeAsync("Ana"));
        Assert.Equal("Salida", await guardar.ObtenerUltimoTipoFichajeAsync("Beto"));
    }

    [Fact]
    public async Task Guardar_redondea_la_precision_y_conserva_ubicacion_motivo_y_foto()
    {
        using var bd = new BaseSqlite();
        var guardar = new FichajesSqlStorage(bd);
        var f = Fichaje("Ana", "Entrada", new DateTime(2026, 11, 2, 8, 0, 0));
        f.Motivo = "  visita cliente  ";
        f.Foto = "2026/11/02/abc.jpg";
        await guardar.GuardarAsync(f);

        var leido = (await guardar.ObtenerFichajesAsync()).Single();
        Assert.Equal(12, leido.Precision);
        Assert.Equal("visita cliente", leido.Motivo);
        Assert.Equal("2026/11/02/abc.jpg", leido.Foto);
        Assert.Equal(-34.6, leido.Latitud);
    }

    [Fact]
    public async Task Un_fichaje_manual_aparece_en_las_liquidaciones_pero_no_cambia_lo_que_toca_fichar()
    {
        using var bd = new BaseSqlite();
        var guardar = new FichajesSqlStorage(bd);
        var repo = new FichajesSqlRepositorio(bd, new MemoryCache(new MemoryCacheOptions()));
        var hoy = DateTime.Now.Date;

        await repo.AgregarManualAsync("Ana", "Entrada", hoy.AddHours(9), "Olvidó fichar", "admin@x.com", hoy.AddHours(10));

        var todos = await repo.ListarAsync();
        Assert.Single(todos);
        Assert.True(todos[0].Corregido);
        Assert.Equal("Manual", todos[0].Lugar);
        Assert.Empty(await guardar.ObtenerFichajesAsync());
        Assert.Equal("Salida", await guardar.ObtenerUltimoTipoFichajeAsync("Ana"));
    }

    [Fact]
    public async Task Corregir_cambia_la_hora_y_deja_constancia_de_la_anterior()
    {
        using var bd = new BaseSqlite();
        var guardar = new FichajesSqlStorage(bd);
        var repo = new FichajesSqlRepositorio(bd, new MemoryCache(new MemoryCacheOptions()));
        await guardar.GuardarAsync(Fichaje("Ana", "Entrada", new DateTime(2026, 11, 2, 8, 0, 0)));
        var id = (await repo.ListarAsync()).Single().Id;

        await repo.CorregirFechaHoraAsync(id, new DateTime(2026, 11, 2, 7, 30, 0), "Llegó antes", "admin@x.com", new DateTime(2026, 11, 2, 18, 0, 0));

        var corregido = (await repo.ObtenerAsync(id))!;
        Assert.Equal(new DateTime(2026, 11, 2, 7, 30, 0), corregido.FechaHora);
        Assert.True(corregido.Corregido);
        Assert.Equal("Llegó antes [antes: 2026-11-02 08:00:00]", corregido.MotivoCorreccion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CorregirFechaHoraAsync(99, DateTime.Now, "x", "y", DateTime.Now));
    }
}

public class ConfiguracionSqlTests
{
    [Fact]
    public async Task Apariencia_guarda_reemplaza_y_vacia()
    {
        using var bd = new BaseSqlite();
        var repo = new AparienciaSqlRepositorio(bd);
        Assert.Null((await repo.ObtenerAsync()).Nombre);

        await repo.GuardarAsync(new AparienciaGuardada { Nombre = "Mi empresa", ColorPrimario = "#112233", Logo = new byte[] { 1, 2, 3 } });
        var a = await repo.ObtenerAsync();
        Assert.Equal("Mi empresa", a.Nombre);
        Assert.Equal("#112233", a.ColorPrimario);
        Assert.Equal(new byte[] { 1, 2, 3 }, a.Logo);

        await repo.GuardarAsync(new AparienciaGuardada { Nombre = "Otra" });
        var b = await repo.ObtenerAsync();
        Assert.Equal("Otra", b.Nombre);
        Assert.Null(b.ColorPrimario);
        Assert.Null(b.Logo);
    }

    [Fact]
    public async Task Configuracion_de_fichaje_es_nula_hasta_que_se_guarda()
    {
        using var bd = new BaseSqlite();
        var repo = new ConfiguracionFichajeSqlRepositorio(bd);
        Assert.Null(await repo.ObtenerAsync());

        await repo.GuardarAsync(new ConfiguracionFichaje { Ubicacion = "SedeFija", Foto = "Obligatoria", Modo = "Kiosco", PrecisionMaxMetros = 80 });
        await repo.GuardarAsync(new ConfiguracionFichaje { Ubicacion = "Libre", Foto = "No", Modo = "Personal", PrecisionMaxMetros = 30 });

        var c = (await repo.ObtenerAsync())!;
        Assert.Equal(("Libre", "No", "Personal", 30), (c.Ubicacion, c.Foto, c.Modo, c.PrecisionMaxMetros));
    }

    [Fact]
    public async Task Sitios_se_leen_en_el_orden_cargado()
    {
        using var bd = new BaseSqlite();
        await using (var db = bd.CreateDbContext())
        {
            db.Sitios.Add(new SitioFila { Nombre = "Sede", Latitud = -34.6, Longitud = -58.4, RadioMetros = 100 });
            db.Sitios.Add(new SitioFila { Nombre = "Depósito", Latitud = -34.7, Longitud = -58.5, RadioMetros = 50 });
            await db.SaveChangesAsync();
        }

        var sitios = await new SitiosSqlRepositorio(bd).ObtenerSitiosAsync();
        Assert.Equal(new[] { "Sede", "Depósito" }, sitios.Select(s => s.Nombre));
        Assert.Equal(50, sitios[1].RadioMetros);
    }
}

public class LiquidacionSqlTests
{
    private static LiquidacionDia Dia(string empleado, int dia, decimal importe) => new()
    {
        EmpleadoId = 1, Empleado = empleado, Fecha = new DateTime(2026, 10, dia), Ingreso = new DateTime(2026, 10, dia, 9, 0, 0),
        Egreso = new DateTime(2026, 10, dia, 17, 0, 0), MinTrabajados = 480, MinPagables = 480, MinNormales = 480,
        ValorHora = 1000.5m, RecargoExtraPct = 50, RecargoFeriadoPct = 100, Importe = importe, Observacion = dia == 2 ? "Feriado" : null
    };

    [Fact]
    public async Task Parametros_tienen_valores_por_defecto_y_se_guardan()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        var inicial = await repo.ObtenerParametrosAsync();
        Assert.Equal((10, 15, 8m, 50m, 100m, 16, false),
            (inicial.ToleranciaMinutos, inicial.RedondeoMinutos, inicial.JornadaNormalHoras, inicial.RecargoExtraPct, inicial.RecargoFeriadoPct, inicial.MaxHorasFichajeAbierto, inicial.PagarPuentes));

        await repo.GuardarParametrosAsync(new ParametrosLiquidacion { ToleranciaMinutos = 5, RedondeoMinutos = 0, JornadaNormalHoras = 6.5m, RecargoExtraPct = 75, RecargoFeriadoPct = 120, MaxHorasFichajeAbierto = 12, PagarPuentes = true });

        var p = await repo.ObtenerParametrosAsync();
        Assert.Equal((5, 0, 6.5m, 75m, 120m, 12, true),
            (p.ToleranciaMinutos, p.RedondeoMinutos, p.JornadaNormalHoras, p.RecargoExtraPct, p.RecargoFeriadoPct, p.MaxHorasFichajeAbierto, p.PagarPuentes));
    }

    [Fact]
    public async Task Valor_hora_se_actualiza_si_ya_hay_uno_para_esa_fecha()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        await repo.GuardarValorHoraAsync(new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1000, VigenteDesde = new DateTime(2026, 10, 1), CargadoPor = "a" });
        await repo.GuardarValorHoraAsync(new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1200, VigenteDesde = new DateTime(2026, 10, 1, 15, 0, 0), CargadoPor = "b" });
        await repo.GuardarValorHoraAsync(new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1500, VigenteDesde = new DateTime(2026, 11, 1), CargadoPor = "b" });

        var valores = await repo.ListarValoresHoraAsync();
        Assert.Equal(new[] { 1200m, 1500m }, valores.Select(v => v.Valor));
        Assert.Equal("b", valores[0].CargadoPor);
    }

    [Fact]
    public async Task Feriados_se_guardan_reemplazan_y_eliminan_por_fecha()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        var fecha = new DateTime(2026, 12, 25);
        await repo.GuardarFeriadoAsync(new Feriado { Fecha = fecha, Descripcion = "Navidad", Tipo = "inamovible", Origen = Feriado.OrigenApi });
        await repo.GuardarFeriadoAsync(new Feriado { Fecha = fecha.AddHours(5), Descripcion = "Navidad (manual)", Origen = Feriado.OrigenManual });

        var uno = (await repo.ListarFeriadosAsync()).Single();
        Assert.Equal("Navidad (manual)", uno.Descripcion);
        Assert.Equal(Feriado.OrigenManual, uno.Origen);

        await repo.EliminarFeriadoAsync(fecha);
        Assert.Empty(await repo.ListarFeriadosAsync());
    }

    [Fact]
    public async Task Aplicar_oficiales_agrega_los_nuevos_y_no_pisa_lo_manual()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        var navidad = new DateTime(2026, 12, 25);
        await repo.GuardarFeriadoAsync(new Feriado { Fecha = navidad, Descripcion = "Navidad de la empresa", Origen = Feriado.OrigenManual });

        var oficiales = new List<Feriado>
        {
            new() { Fecha = navidad, Descripcion = "Navidad", Tipo = "inamovible", Origen = Feriado.OrigenApi },
            new() { Fecha = new DateTime(2027, 1, 1), Descripcion = "Año Nuevo", Tipo = "inamovible", Origen = Feriado.OrigenApi }
        };
        await repo.AplicarOficialesAsync(oficiales, new DateTime(2026, 11, 5));

        var lista = await repo.ListarFeriadosAsync();
        Assert.Equal(2, lista.Count);
        Assert.Equal("Navidad de la empresa", lista.Single(f => f.Fecha == navidad).Descripcion);
        Assert.Equal("Año Nuevo", lista.Single(f => f.Fecha.Year == 2027).Descripcion);
    }

    [Fact]
    public async Task Excedentes_se_aprueban_una_sola_vez_y_se_revocan()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        var dia = new DateTime(2026, 10, 5);
        await repo.AprobarExcedenteAsync("Ana", dia, "admin");
        await repo.AprobarExcedenteAsync("ana", dia.AddHours(3), "admin");
        await repo.AprobarExcedenteAsync("Beto", dia, "admin");

        Assert.Equal(2, (await repo.ListarExcedentesAprobadosAsync()).Count);

        await repo.RevocarExcedenteAsync("ANA", dia);
        Assert.Equal(new[] { ("Beto", dia) }, await repo.ListarExcedentesAprobadosAsync());
    }

    [Fact]
    public async Task Cerrar_una_liquidacion_guarda_el_periodo_con_sus_dias_y_se_puede_marcar_pagada()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        var periodo = new PeriodoLiquidado { Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 10, 15), CerradaPor = "admin", FechaCierre = new DateTime(2026, 10, 16, 10, 0, 0) };

        var id = await repo.GuardarLiquidacionAsync(periodo, new[] { Dia("Beto", 3, 8004m), Dia("Ana", 2, 8004m), Dia("Ana", 1, 8004m) });

        Assert.Equal(id, periodo.Id);
        var dias = await repo.ListarDiasDeLiquidacionAsync(id);
        Assert.Equal(new[] { ("Ana", 1), ("Ana", 2), ("Beto", 3) }, dias.Select(d => (d.Empleado, d.Fecha.Day)));
        Assert.Equal("Feriado", dias[1].Observacion);
        Assert.Equal(1000.5m, dias[0].ValorHora);

        Assert.True(await repo.HayLiquidacionEnRangoAsync(new DateTime(2026, 10, 15), new DateTime(2026, 10, 20)));
        Assert.False(await repo.HayLiquidacionEnRangoAsync(new DateTime(2026, 10, 16), new DateTime(2026, 10, 20)));

        await repo.MarcarPagadaAsync(id, "admin2", new DateTime(2026, 10, 20));
        var guardada = (await repo.ListarLiquidacionesAsync()).Single();
        Assert.Equal(("Pagada", "admin2"), (guardada.Estado, guardada.PagadaPor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MarcarPagadaAsync(99, "x", DateTime.Now));
    }

    [Fact]
    public async Task Un_periodo_que_se_superpone_se_rechaza_y_no_deja_nada_guardado()
    {
        using var bd = new BaseSqlite();
        var repo = new LiquidacionSqlRepositorio(bd);
        await repo.GuardarLiquidacionAsync(
            new PeriodoLiquidado { Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 10, 15), CerradaPor = "a", FechaCierre = DateTime.Now },
            new[] { Dia("Ana", 1, 100m) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.GuardarLiquidacionAsync(
            new PeriodoLiquidado { Desde = new DateTime(2026, 10, 10), Hasta = new DateTime(2026, 10, 31), CerradaPor = "a", FechaCierre = DateTime.Now },
            new[] { Dia("Ana", 20, 100m), Dia("Ana", 21, 100m) }));

        Assert.Single(await repo.ListarLiquidacionesAsync());
        await using var db = bd.CreateDbContext();
        Assert.Equal(1, await db.LiquidacionDias.CountAsync());
    }
}

public class InicializadorSqlTests
{
    private static (InicializadorSql Inicializador, RolesSqlRepositorio Roles, EmpleadosSqlRepositorio Empleados) Armar(BaseSqlite bd, Dictionary<string, string?> config)
    {
        var roles = new RolesSqlRepositorio(bd);
        var empleados = new EmpleadosSqlRepositorio(bd);
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var inicializador = new InicializadorSql(bd, roles, empleados, new PermisosService(roles, empleados), configuracion, NullLogger<InicializadorSql>.Instance);
        return (inicializador, roles, empleados);
    }

    // Con SQLite no hay migraciones aplicadas (la base se crea del modelo): se apaga ese paso
    private static Dictionary<string, string?> Config(params (string, string)[] extra)
    {
        var c = new Dictionary<string, string?> { ["Datos:Migrar"] = "false" };
        foreach (var (k, v) in extra) c[k] = v;
        return c;
    }

    [Fact]
    public async Task En_una_base_vacia_siembra_permisos_roles_parametros_y_el_primer_administrador()
    {
        using var bd = new BaseSqlite();
        var (inicializador, roles, empleados) = Armar(bd, Config(("Setup:AdminNombre", "Gisela"), ("Setup:AdminEmail", "gisela@x.com"), ("Setup:AdminPin", "1234")));

        await inicializador.InicializarAsync();

        Assert.Equal(CatalogoPermisos.Todos.Count(), (await roles.ListarPermisosAsync()).Count);
        Assert.Equal(CatalogoPermisos.RolesIniciales.Count(), (await roles.ListarRolesAsync()).Count);

        var admin = (await empleados.BuscarPorEmailAsync("gisela@x.com"))!;
        Assert.True(BCrypt.Net.BCrypt.Verify("1234", admin.PinHash));

        var rolAdmin = (await roles.ListarRolesAsync()).Single(r => r.Nombre == CatalogoPermisos.RolAdministrador);
        Assert.True(rolAdmin.Protegido);
        Assert.Contains(rolAdmin.Id, (await roles.ListarAsignacionesAsync())[admin.Id]);

        var permisosDelAdmin = (await roles.ListarRolPermisosAsync()).Where(rp => rp.RolId == rolAdmin.Id).Count();
        Assert.Equal(CatalogoPermisos.Todos.Count(), permisosDelAdmin);

        await using var db = bd.CreateDbContext();
        Assert.True(await db.Parametros.AnyAsync());
    }

    [Fact]
    public async Task Es_idempotente_y_no_crea_otro_administrador_si_ya_hay_empleados()
    {
        using var bd = new BaseSqlite();
        var (inicializador, roles, empleados) = Armar(bd, Config(("Setup:AdminNombre", "Gisela"), ("Setup:AdminEmail", "gisela@x.com"), ("Setup:AdminPin", "1234")));

        await inicializador.InicializarAsync();
        var permisos = (await roles.ListarPermisosAsync()).Count;
        var rolesCantidad = (await roles.ListarRolesAsync()).Count;
        await inicializador.InicializarAsync();

        Assert.Equal(permisos, (await roles.ListarPermisosAsync()).Count);
        Assert.Equal(rolesCantidad, (await roles.ListarRolesAsync()).Count);
        Assert.Single(await empleados.ListarAsync());
    }

    [Fact]
    public async Task Sin_datos_del_administrador_no_crea_empleados()
    {
        using var bd = new BaseSqlite();
        var (inicializador, _, empleados) = Armar(bd, Config());

        await inicializador.InicializarAsync();

        Assert.Empty(await empleados.ListarAsync());
    }

    [Fact]
    public async Task Un_pin_invalido_no_crea_el_administrador()
    {
        using var bd = new BaseSqlite();
        var (inicializador, _, empleados) = Armar(bd, Config(("Setup:AdminNombre", "Gisela"), ("Setup:AdminEmail", "gisela@x.com"), ("Setup:AdminPin", "12")));

        await inicializador.InicializarAsync();

        Assert.Empty(await empleados.ListarAsync());
    }
}
