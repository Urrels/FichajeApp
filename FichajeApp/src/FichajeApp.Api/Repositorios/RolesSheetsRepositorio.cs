using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Repositorios;

// Hojas: Roles (Id|Nombre|PadreId|Protegido), Permisos (Id|Nombre),
//        RolPermiso (RolId|PermisoId), UsuarioRol (EmpleadoId|RolId)
public class RolesSheetsRepositorio : IRolesRepositorio
{
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly HojaSheets _roles;
    private readonly HojaSheets _permisos;
    private readonly HojaSheets _rolPermiso;
    private readonly HojaSheets _usuarioRol;

    public RolesSheetsRepositorio(GoogleSheetsClient cliente)
    {
        _roles = new HojaSheets(cliente, Planilla.Roles);
        _permisos = new HojaSheets(cliente, Planilla.Permisos);
        _rolPermiso = new HojaSheets(cliente, Planilla.RolPermiso);
        _usuarioRol = new HojaSheets(cliente, Planilla.UsuarioRol);
    }

    public async Task<List<Rol>> ListarRolesAsync() =>
        (await _roles.LeerAsync("A:D")).Select(f => new Rol
        {
            Id = f.Entero(0),
            Nombre = f.Texto(1),
            PadreId = f.EnteroONulo(2),
            Protegido = f.Booleano(3)
        }).ToList();

    public async Task<List<Permiso>> ListarPermisosAsync() =>
        (await _permisos.LeerAsync("A:B")).Select(f => new Permiso { Id = f.Entero(0), Nombre = f.Texto(1) }).ToList();

    public async Task<List<(int RolId, int PermisoId)>> ListarRolPermisosAsync() =>
        (await _rolPermiso.LeerAsync("A:B")).Select(f => (f.Entero(0), f.Entero(1))).ToList();

    public async Task<int> InsertarRolAsync(string nombre, int? padreId, bool protegido = false)
    {
        await _escritura.WaitAsync();
        try
        {
            var existentes = await ListarRolesAsync();
            int id = existentes.Count == 0 ? 1 : existentes.Max(r => r.Id) + 1;
            await _roles.AgregarAsync(new List<object> { id, nombre, padreId?.ToString() ?? "", protegido });
            return id;
        }
        finally { _escritura.Release(); }
    }

    public async Task CambiarPadreAsync(int rolId, int? padreId)
    {
        await _escritura.WaitAsync();
        try
        {
            var fila = (await _roles.LeerAsync("A:D")).FirstOrDefault(f => f.Entero(0) == rolId)
                ?? throw new InvalidOperationException("El rol no existe.");
            await _roles.ActualizarAsync(fila.Numero,
                new List<object> { rolId, fila.Texto(1), padreId?.ToString() ?? "", fila.Booleano(3) });
        }
        finally { _escritura.Release(); }
    }

    public async Task EliminarRolesAsync(IReadOnlyCollection<int> rolIds)
    {
        await _escritura.WaitAsync();
        try
        {
            var ids = rolIds.ToHashSet();

            var roles = (await _roles.LeerAsync("A:D")).Where(f => !ids.Contains(f.Entero(0)))
                .Select(f => (IList<object>)new List<object> { f.Entero(0), f.Texto(1), f.Texto(2), f.Booleano(3) });
            var rolPermiso = (await _rolPermiso.LeerAsync("A:B")).Where(f => !ids.Contains(f.Entero(0)))
                .Select(f => (IList<object>)new List<object> { f.Entero(0), f.Entero(1) });
            var usuarioRol = (await _usuarioRol.LeerAsync("A:B")).Where(f => !ids.Contains(f.Entero(1)))
                .Select(f => (IList<object>)new List<object> { f.Entero(0), f.Entero(1) });

            // Primero las tablas de relación y por último el rol, para no dejar relaciones colgando
            await _rolPermiso.ReemplazarTodoAsync(rolPermiso.ToList());
            await _usuarioRol.ReemplazarTodoAsync(usuarioRol.ToList());
            await _roles.ReemplazarTodoAsync(roles.ToList());
        }
        finally { _escritura.Release(); }
    }

    public async Task GuardarPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds)
    {
        await _escritura.WaitAsync();
        try
        {
            var otros = (await _rolPermiso.LeerAsync("A:B")).Where(f => f.Entero(0) != rolId)
                .Select(f => (IList<object>)new List<object> { f.Entero(0), f.Entero(1) }).ToList();
            otros.AddRange(permisoIds.Distinct().Select(p => (IList<object>)new List<object> { rolId, p }));
            await _rolPermiso.ReemplazarTodoAsync(otros);
        }
        finally { _escritura.Release(); }
    }

    public async Task<Dictionary<int, List<int>>> ListarAsignacionesAsync()
    {
        var resultado = new Dictionary<int, List<int>>();
        foreach (var f in await _usuarioRol.LeerAsync("A:B"))
        {
            int empleado = f.Entero(0);
            if (!resultado.TryGetValue(empleado, out var lista))
                resultado[empleado] = lista = new List<int>();
            lista.Add(f.Entero(1));
        }
        return resultado;
    }

    public async Task ReemplazarAsignacionesAsync(int empleadoId, IEnumerable<int> rolIds)
    {
        await _escritura.WaitAsync();
        try
        {
            var otros = (await _usuarioRol.LeerAsync("A:B")).Where(f => f.Entero(0) != empleadoId)
                .Select(f => (IList<object>)new List<object> { f.Entero(0), f.Entero(1) }).ToList();
            otros.AddRange(rolIds.Distinct().Select(r => (IList<object>)new List<object> { empleadoId, r }));
            await _usuarioRol.ReemplazarTodoAsync(otros);
        }
        finally { _escritura.Release(); }
    }

    public async Task InsertarPermisoAsync(string nombre)
    {
        await _escritura.WaitAsync();
        try
        {
            var existentes = await ListarPermisosAsync();
            int id = existentes.Count == 0 ? 1 : existentes.Max(p => p.Id) + 1;
            await _permisos.AgregarAsync(new List<object> { id, nombre });
        }
        finally { _escritura.Release(); }
    }
}
