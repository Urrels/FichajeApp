using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Repositorios en memoria: permiten probar los servicios sin Google Sheets.

public class RelojFalso : TimeProvider
{
    public DateTime Ahora { get; set; } = new(2026, 11, 2, 12, 0, 0);
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Ahora, DateTimeKind.Utc));
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public class EmpleadosFalso : IEmpleadosRepositorio
{
    public List<EmpleadoDto> Datos { get; } = new();
    public Task<List<EmpleadoDto>> ListarAsync() => Task.FromResult(Datos.Select(Copia).ToList());
    public Task<EmpleadoDto?> ObtenerPorIdAsync(int id) => Task.FromResult(Datos.Where(e => e.Id == id).Select(Copia).FirstOrDefault());
    public Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false) =>
        Task.FromResult(Datos.Where(e => e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && (incluirInactivos || e.Activo))
            .Select(Copia).FirstOrDefault());

    public Task<EmpleadoDto> CrearAsync(EmpleadoDto e)
    {
        e.Id = Datos.Count == 0 ? 1 : Datos.Max(x => x.Id) + 1;
        Datos.Add(Copia(e));
        return Task.FromResult(e);
    }

    public Task ActualizarAsync(EmpleadoDto e)
    {
        Datos[Datos.FindIndex(x => x.Id == e.Id)] = Copia(e);
        return Task.CompletedTask;
    }

    // Copias, como una hoja real: modificar el objeto devuelto no cambia lo guardado hasta llamar a Actualizar
    private static EmpleadoDto Copia(EmpleadoDto e) => new()
    {
        Id = e.Id, NombreCompleto = e.NombreCompleto, Email = e.Email, PinHash = e.PinHash, Activo = e.Activo,
        Rol = e.Rol, IntentosFallidos = e.IntentosFallidos, Bloqueado = e.Bloqueado, Fila = e.Fila
    };
}

public class RolesFalso : IRolesRepositorio
{
    public List<Rol> Roles { get; } = new();
    public List<Permiso> Permisos { get; } = new();
    public List<(int RolId, int PermisoId)> RolPermisos { get; } = new();
    public Dictionary<int, List<int>> Asignaciones { get; } = new();

    public Task<List<Rol>> ListarRolesAsync() => Task.FromResult(Roles.Select(r => new Rol { Id = r.Id, Nombre = r.Nombre, PadreId = r.PadreId, Protegido = r.Protegido }).ToList());
    public Task<List<Permiso>> ListarPermisosAsync() => Task.FromResult(Permisos.ToList());
    public Task<List<(int RolId, int PermisoId)>> ListarRolPermisosAsync() => Task.FromResult(RolPermisos.ToList());

    public Task<int> InsertarRolAsync(string nombre, int? padreId, bool protegido = false)
    {
        int id = Roles.Count == 0 ? 1 : Roles.Max(r => r.Id) + 1;
        Roles.Add(new Rol { Id = id, Nombre = nombre, PadreId = padreId, Protegido = protegido });
        return Task.FromResult(id);
    }

    public Task CambiarPadreAsync(int rolId, int? padreId)
    {
        Roles.First(r => r.Id == rolId).PadreId = padreId;
        return Task.CompletedTask;
    }

    public Task EliminarRolesAsync(IReadOnlyCollection<int> rolIds)
    {
        Roles.RemoveAll(r => rolIds.Contains(r.Id));
        RolPermisos.RemoveAll(rp => rolIds.Contains(rp.RolId));
        foreach (var lista in Asignaciones.Values) lista.RemoveAll(rolIds.Contains);
        foreach (var vacio in Asignaciones.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) Asignaciones.Remove(vacio);
        return Task.CompletedTask;
    }

    public Task GuardarPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds)
    {
        RolPermisos.RemoveAll(rp => rp.RolId == rolId);
        RolPermisos.AddRange(permisoIds.Distinct().Select(p => (rolId, p)));
        return Task.CompletedTask;
    }

    public Task<Dictionary<int, List<int>>> ListarAsignacionesAsync() =>
        Task.FromResult(Asignaciones.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));

    public Task ReemplazarAsignacionesAsync(int empleadoId, IEnumerable<int> rolIds)
    {
        var lista = rolIds.Distinct().ToList();
        if (lista.Count == 0) Asignaciones.Remove(empleadoId); else Asignaciones[empleadoId] = lista;
        return Task.CompletedTask;
    }

    public Task InsertarPermisoAsync(string nombre)
    {
        Permisos.Add(new Permiso { Id = Permisos.Count == 0 ? 1 : Permisos.Max(p => p.Id) + 1, Nombre = nombre });
        return Task.CompletedTask;
    }
}

public class BitacoraFalso : IBitacoraRepositorio
{
    public List<EntradaBitacora> Datos { get; } = new();
    public Task RegistrarAsync(EntradaBitacora e) { Datos.Add(e); return Task.CompletedTask; }
    public Task<List<EntradaBitacora>> ListarAsync() => Task.FromResult(Datos.ToList());
}

public class HistorialFalso : IHistorialUsuariosRepositorio
{
    public List<HistorialUsuario> Datos { get; } = new();

    public Task<int> RegistrarAsync(HistorialUsuario f)
    {
        f.Id = Datos.Count + 1;
        Datos.Add(f);
        return Task.FromResult(f.Id);
    }

    public Task<List<HistorialUsuario>> ListarAsync(int? empleadoId = null) =>
        Task.FromResult(Datos.Where(h => empleadoId == null || h.EmpleadoId == empleadoId).OrderByDescending(h => h.Id).ToList());

    public Task<HistorialUsuario?> ObtenerAsync(int id) => Task.FromResult(Datos.FirstOrDefault(h => h.Id == id));
}

public class FichajesFalso : IFichajesRepositorio
{
    public List<FichajeRegistrado> Datos { get; } = new();

    public void Agregar(string quien, string tipo, DateTime cuando) =>
        Datos.Add(new FichajeRegistrado { Id = Datos.Count + 2, NombreCompleto = quien, Tipo = tipo, FechaHora = cuando });

    public Task<List<FichajeRegistrado>> ListarAsync() => Task.FromResult(Datos.ToList());
    public Task<FichajeRegistrado?> ObtenerAsync(int id) => Task.FromResult(Datos.FirstOrDefault(f => f.Id == id));

    public Task CorregirFechaHoraAsync(int id, DateTime nueva, string motivo, string por, DateTime ahora)
    {
        var f = Datos.First(x => x.Id == id);
        f.FechaHora = nueva;
        f.Corregido = true;
        f.MotivoCorreccion = motivo;
        return Task.CompletedTask;
    }

    public Task AgregarManualAsync(string empleado, string tipo, DateTime fechaHora, string motivo, string por, DateTime ahora)
    {
        Datos.Add(new FichajeRegistrado
        {
            Id = Datos.Count + 2, NombreCompleto = empleado, Tipo = tipo, FechaHora = fechaHora, Corregido = true, MotivoCorreccion = motivo
        });
        return Task.CompletedTask;
    }
}

public class LiquidacionFalso : ILiquidacionRepositorio
{
    public ParametrosLiquidacion Parametros { get; set; } = new();
    public List<ValorHoraEmpleado> Valores { get; } = new();
    public List<Feriado> Feriados { get; } = new();
    public List<(string Empleado, DateTime Fecha)> Excedentes { get; } = new();
    public List<PeriodoLiquidado> Periodos { get; } = new();
    public Dictionary<int, List<LiquidacionDia>> Dias { get; } = new();

    public Task<ParametrosLiquidacion> ObtenerParametrosAsync() => Task.FromResult(Parametros);
    public Task GuardarParametrosAsync(ParametrosLiquidacion p) { Parametros = p; return Task.CompletedTask; }
    public Task<List<ValorHoraEmpleado>> ListarValoresHoraAsync() => Task.FromResult(Valores.ToList());

    public Task GuardarValorHoraAsync(ValorHoraEmpleado v)
    {
        Valores.RemoveAll(x => x.EmpleadoId == v.EmpleadoId && x.VigenteDesde.Date == v.VigenteDesde.Date);
        Valores.Add(v);
        return Task.CompletedTask;
    }

    public Task<List<Feriado>> ListarFeriadosAsync() => Task.FromResult(Feriados.ToList());
    public Task GuardarFeriadoAsync(Feriado f) { Feriados.RemoveAll(x => x.Fecha == f.Fecha); Feriados.Add(f); return Task.CompletedTask; }
    public Task EliminarFeriadoAsync(DateTime fecha) { Feriados.RemoveAll(x => x.Fecha == fecha); return Task.CompletedTask; }

    public Task<List<(string Empleado, DateTime Fecha)>> ListarExcedentesAprobadosAsync() => Task.FromResult(Excedentes.ToList());
    public Task AprobarExcedenteAsync(string e, DateTime f, string por) { Excedentes.Add((e, f)); return Task.CompletedTask; }
    public Task RevocarExcedenteAsync(string e, DateTime f) { Excedentes.RemoveAll(x => x.Empleado == e && x.Fecha == f); return Task.CompletedTask; }

    public Task<List<PeriodoLiquidado>> ListarLiquidacionesAsync() => Task.FromResult(Periodos.ToList());
    public Task<bool> HayLiquidacionEnRangoAsync(DateTime desde, DateTime hasta) =>
        Task.FromResult(Periodos.Any(p => p.Desde <= hasta.Date && p.Hasta >= desde.Date));

    public Task<int> GuardarLiquidacionAsync(PeriodoLiquidado p, IReadOnlyCollection<LiquidacionDia> dias)
    {
        p.Id = Periodos.Count + 1;
        Periodos.Add(p);
        Dias[p.Id] = dias.ToList();
        return Task.FromResult(p.Id);
    }

    public Task MarcarPagadaAsync(int id, string por, DateTime cuando)
    {
        var p = Periodos.First(x => x.Id == id);
        p.Estado = "Pagada"; p.PagadaPor = por; p.FechaPago = cuando;
        return Task.CompletedTask;
    }

    public Task<List<LiquidacionDia>> ListarDiasDeLiquidacionAsync(int id) => Task.FromResult(Dias.GetValueOrDefault(id) ?? new());
}

// Arma todos los servicios sobre repositorios en memoria, con los roles y permisos iniciales del sistema
public class Entorno
{
    public EmpleadosFalso Empleados { get; } = new();
    public RolesFalso Roles { get; } = new();
    public BitacoraFalso BitacoraRepo { get; } = new();
    public HistorialFalso HistorialRepo { get; } = new();
    public FichajesFalso Fichajes { get; } = new();
    public LiquidacionFalso LiquidacionRepo { get; } = new();
    public RelojFalso Reloj { get; } = new();

    public BitacoraService Bitacora { get; }
    public PermisosService Permisos { get; }
    public GuardaAdministracion Guarda { get; }
    public HistorialUsuariosService Historial { get; }
    public LoginService Login { get; }
    public UsuariosAdminService Usuarios { get; }
    public RolesService RolesServicio { get; }
    public JornadasService Jornadas { get; }
    public FichajesAdminService FichajesAdmin { get; }
    public LiquidacionService Liquidacion { get; }

    public const string PinAdmin = "1111", PinAna = "2222", PinBeto = "3333";

    public Entorno()
    {
        // Catálogo y roles iniciales: igual que el InicializadorPlanilla
        foreach (var p in CatalogoPermisos.Todos) Roles.InsertarPermisoAsync(p).Wait();
        var idPermiso = Roles.Permisos.ToDictionary(p => p.Nombre, p => p.Id);
        foreach (var (nombre, permisos) in CatalogoPermisos.RolesIniciales)
        {
            int id = Roles.InsertarRolAsync(nombre, null, nombre == CatalogoPermisos.RolAdministrador).Result;
            Roles.GuardarPermisosDeRolAsync(id, permisos.Select(p => idPermiso[p])).Wait();
        }

        Empleados.Datos.Add(Persona(1, "Admin Prueba", "admin@test.com", PinAdmin, "Administrador"));
        Empleados.Datos.Add(Persona(2, "Ana Empleada", "ana@test.com", PinAna, ""));
        Empleados.Datos.Add(Persona(3, "Beto Supervisor", "beto@test.com", PinBeto, "Supervisor"));

        Bitacora = new BitacoraService(BitacoraRepo, Reloj);
        Permisos = new PermisosService(Roles, Empleados);
        Guarda = new GuardaAdministracion(Roles, Permisos);
        Historial = new HistorialUsuariosService(HistorialRepo, Roles, Reloj);
        Login = new LoginService(Empleados, Permisos, Bitacora, Historial);
        Usuarios = new UsuariosAdminService(Empleados, Roles, Fichajes, Historial, HistorialRepo, Guarda, Bitacora);
        RolesServicio = new RolesService(Roles, Guarda, Bitacora);
        Jornadas = new JornadasService(Fichajes, Empleados);
        FichajesAdmin = new FichajesAdminService(Jornadas, Fichajes, Empleados, LiquidacionRepo, Bitacora, Reloj);
        Liquidacion = new LiquidacionService(Jornadas, LiquidacionRepo, Empleados, Bitacora, Reloj);
    }

    // Costo 4 (el mínimo de BCrypt) para que los tests no tarden
    private static EmpleadoDto Persona(int id, string nombre, string email, string pin, string rol) => new()
    {
        Id = id, NombreCompleto = nombre, Email = email, PinHash = BCrypt.Net.BCrypt.HashPassword(pin, 4), Activo = true, Rol = rol
    };

    public int IdRol(string nombre) => Roles.Roles.First(r => r.Nombre == nombre).Id;
}
