using Microsoft.EntityFrameworkCore;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Persistencia.Sql;

// Tabla Empleados (el decorador con caché se pone encima).
public class EmpleadosSqlRepositorio : BaseSql, IEmpleadosRepositorio
{
    public EmpleadosSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<List<EmpleadoDto>> ListarAsync() => LeerAsync(async db =>
        (await db.Empleados.OrderBy(e => e.Id).ToListAsync()).Select(Mapear).ToList());

    public Task<EmpleadoDto?> ObtenerPorIdAsync(int id) => LeerAsync(async db =>
    {
        var fila = await db.Empleados.FirstOrDefaultAsync(e => e.Id == id);
        return fila is null ? null : Mapear(fila);
    });

    // La comparación no distingue mayúsculas, (en SQL Server ya es así; ToLower también lo asegura en otros motores)
    public Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false) => LeerAsync(async db =>
    {
        var buscado = (email ?? string.Empty).Trim().ToLower();
        var fila = await db.Empleados.FirstOrDefaultAsync(e => e.Email.ToLower() == buscado && (incluirInactivos || e.Activo));
        return fila is null ? null : Mapear(fila);
    });

    public async Task<EmpleadoDto> CrearAsync(EmpleadoDto empleado)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = new EmpleadoFila();
        Copiar(empleado, fila);
        db.Empleados.Add(fila);
        await db.SaveChangesAsync();
        empleado.Id = fila.Id;
        return empleado;
    }

    public async Task ActualizarAsync(EmpleadoDto empleado)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = await db.Empleados.FirstOrDefaultAsync(e => e.Id == empleado.Id)
            ?? throw new InvalidOperationException("El empleado no existe.");
        Copiar(empleado, fila);
        await db.SaveChangesAsync();
    }

    private static void Copiar(EmpleadoDto e, EmpleadoFila f)
    {
        f.NombreCompleto = e.NombreCompleto;
        f.Email = e.Email;
        f.PinHash = e.PinHash;
        f.Activo = e.Activo;
        f.Rol = string.IsNullOrWhiteSpace(e.Rol) ? "Empleado" : e.Rol;
        f.IntentosFallidos = e.IntentosFallidos;
        f.Bloqueado = e.Bloqueado;
        f.SupervisorId = e.SupervisorId;
    }

    private static EmpleadoDto Mapear(EmpleadoFila f) => new()
    {
        Id = f.Id, NombreCompleto = f.NombreCompleto, Email = f.Email, PinHash = f.PinHash, Activo = f.Activo,
        Rol = f.Rol, IntentosFallidos = f.IntentosFallidos, Bloqueado = f.Bloqueado, SupervisorId = f.SupervisorId
    };
}

// Tablas Roles, Permisos, RolPermiso y UsuarioRol
public class RolesSqlRepositorio : BaseSql, IRolesRepositorio
{
    public RolesSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<List<Rol>> ListarRolesAsync() => LeerAsync(async db =>
        (await db.Roles.OrderBy(r => r.Id).ToListAsync())
            .Select(r => new Rol { Id = r.Id, Nombre = r.Nombre, PadreId = r.PadreId, Protegido = r.Protegido }).ToList());

    public Task<List<Permiso>> ListarPermisosAsync() => LeerAsync(async db =>
        (await db.Permisos.OrderBy(p => p.Id).ToListAsync())
            .Select(p => new Permiso { Id = p.Id, Nombre = p.Nombre }).ToList());

    public Task<List<(int RolId, int PermisoId)>> ListarRolPermisosAsync() => LeerAsync(async db =>
        (await db.RolPermisos.ToListAsync()).Select(rp => (rp.RolId, rp.PermisoId)).ToList());

    public async Task<int> InsertarRolAsync(string nombre, int? padreId, bool protegido = false)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = new RolFila { Nombre = nombre, PadreId = padreId, Protegido = protegido };
        db.Roles.Add(fila);
        await db.SaveChangesAsync();
        return fila.Id;
    }

    public async Task CambiarPadreAsync(int rolId, int? padreId)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = await db.Roles.FirstOrDefaultAsync(r => r.Id == rolId) ?? throw new InvalidOperationException("El rol no existe.");
        fila.PadreId = padreId;
        await db.SaveChangesAsync();
    }

    // Primero las relaciones y por último el rol, todo en una transacción: no quedan relaciones colgando
    public Task EliminarRolesAsync(IReadOnlyCollection<int> rolIds)
    {
        var ids = rolIds.ToList();
        return EnTransaccionAsync(async db =>
        {
            await db.RolPermisos.Where(rp => ids.Contains(rp.RolId)).ExecuteDeleteAsync();
            await db.UsuarioRoles.Where(ur => ids.Contains(ur.RolId)).ExecuteDeleteAsync();
            await db.Roles.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();
        });
    }

    public Task GuardarPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds)
    {
        var nuevos = permisoIds.Distinct().ToList();
        return EnTransaccionAsync(async db =>
        {
            await db.RolPermisos.Where(rp => rp.RolId == rolId).ExecuteDeleteAsync();
            db.RolPermisos.AddRange(nuevos.Select(p => new RolPermisoFila { RolId = rolId, PermisoId = p }));
            await db.SaveChangesAsync();
        });
    }

    public Task<Dictionary<int, List<int>>> ListarAsignacionesAsync() => LeerAsync(async db =>
        (await db.UsuarioRoles.ToListAsync()).GroupBy(ur => ur.EmpleadoId)
            .ToDictionary(g => g.Key, g => g.Select(ur => ur.RolId).ToList()));

    public Task ReemplazarAsignacionesAsync(int empleadoId, IEnumerable<int> rolIds)
    {
        var nuevos = rolIds.Distinct().ToList();
        return EnTransaccionAsync(async db =>
        {
            await db.UsuarioRoles.Where(ur => ur.EmpleadoId == empleadoId).ExecuteDeleteAsync();
            db.UsuarioRoles.AddRange(nuevos.Select(r => new UsuarioRolFila { EmpleadoId = empleadoId, RolId = r }));
            await db.SaveChangesAsync();
        });
    }

    public async Task InsertarPermisoAsync(string nombre)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.Permisos.Add(new PermisoFila { Nombre = nombre });
        await db.SaveChangesAsync();
    }
}

// Tablas Bitacora y UsuarioHistorial: solo se agregan filas
public class BitacoraSqlRepositorio : BaseSql, IBitacoraRepositorio
{
    public BitacoraSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task RegistrarAsync(EntradaBitacora e)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.Bitacora.Add(new BitacoraFila { FechaHora = e.FechaHora, Usuario = e.Usuario, Accion = e.Accion });
        await db.SaveChangesAsync();
    }

    public Task<List<EntradaBitacora>> ListarAsync() => LeerAsync(async db =>
        (await db.Bitacora.OrderBy(b => b.Id).ToListAsync())
            .Select(b => new EntradaBitacora { FechaHora = b.FechaHora, Usuario = b.Usuario, Accion = b.Accion }).ToList());
}

public class HistorialUsuariosSqlRepositorio : BaseSql, IHistorialUsuariosRepositorio
{
    public HistorialUsuariosSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task<int> RegistrarAsync(HistorialUsuario foto)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = new HistorialFila
        {
            FechaHora = foto.FechaHora, EmpleadoId = foto.EmpleadoId, Tipo = foto.Tipo, RealizadoPor = foto.RealizadoPor,
            Nombre = foto.Nombre, Email = foto.Email, Activo = foto.Activo, Bloqueado = foto.Bloqueado,
            IntentosFallidos = foto.IntentosFallidos, RolIds = string.Join(",", foto.RolIds.OrderBy(r => r)),
            VersionOrigen = foto.VersionOrigen
        };
        db.Historial.Add(fila);
        await db.SaveChangesAsync();
        foto.Id = fila.Id;
        return fila.Id;
    }

    public Task<List<HistorialUsuario>> ListarAsync(int? empleadoId = null) => LeerAsync(async db =>
    {
        var consulta = db.Historial.AsQueryable();
        if (empleadoId.HasValue) consulta = consulta.Where(h => h.EmpleadoId == empleadoId.Value);
        return (await consulta.OrderByDescending(h => h.FechaHora).ThenByDescending(h => h.Id).ToListAsync())
            .Select(Mapear).ToList();
    });

    public Task<HistorialUsuario?> ObtenerAsync(int id) => LeerAsync(async db =>
    {
        var fila = await db.Historial.FirstOrDefaultAsync(h => h.Id == id);
        return fila is null ? null : Mapear(fila);
    });

    private static HistorialUsuario Mapear(HistorialFila f) => new()
    {
        Id = f.Id, FechaHora = f.FechaHora, EmpleadoId = f.EmpleadoId, Tipo = f.Tipo, RealizadoPor = f.RealizadoPor,
        Nombre = f.Nombre, Email = f.Email, Activo = f.Activo, Bloqueado = f.Bloqueado, IntentosFallidos = f.IntentosFallidos,
        RolIds = f.RolIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList(),
        VersionOrigen = f.VersionOrigen
    };
}
