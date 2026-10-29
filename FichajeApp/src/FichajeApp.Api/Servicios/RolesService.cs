using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

public class RolResumenDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class NodoRolDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? PadreId { get; set; }
    public bool EsRol { get; set; }
    public bool Protegido { get; set; }
    public List<NodoRolDto> Hijos { get; set; } = new();
}

// Gestión de roles y permisos (Composite). Las reglas puras viven en ReglasRoles; acá solo se orquestan
// con el acceso a datos, la bitácora y la guarda anti-lockout.
public class RolesService
{
    private const int LargoMaximoNombre = 50;
    private static readonly SemaphoreSlim _cambios = new(1, 1); // un cambio de roles a la vez

    private readonly IRolesRepositorio _roles;
    private readonly GuardaAdministracion _guarda;
    private readonly BitacoraService _bitacora;

    public RolesService(IRolesRepositorio roles, GuardaAdministracion guarda, BitacoraService bitacora)
    {
        _roles = roles;
        _guarda = guarda;
        _bitacora = bitacora;
    }

    public async Task<List<NodoPermiso>> ObtenerArbolAsync()
    {
        var roles = await _roles.ListarRolesAsync();
        var permisos = (await _roles.ListarPermisosAsync()).ToDictionary(p => p.Id);
        var rolPermisos = (await _roles.ListarRolPermisosAsync())
            .Where(rp => permisos.ContainsKey(rp.PermisoId))
            .Select(rp => (rp.RolId, (Permiso)permisos[rp.PermisoId].Clonar()))
            .ToList();
        return ReglasRoles.ArmarArbol(roles, rolPermisos);
    }

    public async Task<List<NodoRolDto>> ObtenerArbolDtoAsync() =>
        (await ObtenerArbolAsync()).Select(AUno).ToList();

    public Task<List<Permiso>> ListarPermisosDisponiblesAsync() => _roles.ListarPermisosAsync();

    public async Task<List<RolResumenDto>> ListarRolesAsync() =>
        (await _roles.ListarRolesAsync()).OrderBy(r => r.Nombre).Select(r => new RolResumenDto { Id = r.Id, Nombre = r.Nombre }).ToList();

    public async Task<List<int>> ObtenerPermisoIdsDeRolAsync(int rolId) =>
        (await _roles.ListarRolPermisosAsync()).Where(rp => rp.RolId == rolId).Select(rp => rp.PermisoId).ToList();

    public async Task<int> AgregarAsync(string actor, string nombre, int? padreId)
    {
        await _cambios.WaitAsync();
        try
        {
            var todos = await _roles.ListarRolesAsync();
            nombre = ValidarNombre(nombre, todos);
            if (padreId.HasValue && todos.All(r => r.Id != padreId.Value))
                throw new InvalidOperationException("El rol padre no existe.");

            int id = await _roles.InsertarRolAsync(nombre, padreId);
            await _bitacora.RegistrarAsync(actor, "ROL_CREADO:" + nombre);
            return id;
        }
        finally { _cambios.Release(); }
    }

    // Prototype: copia el rol (sin sus subroles), con sus propios permisos, bajo el mismo padre
    public async Task<int> DuplicarAsync(string actor, int rolId, string nuevoNombre)
    {
        await _cambios.WaitAsync();
        try
        {
            var todos = await _roles.ListarRolesAsync();
            nuevoNombre = ValidarNombre(nuevoNombre, todos);

            var original = ReglasRoles.BuscarRol(await ObtenerArbolAsync(), rolId)
                ?? throw new InvalidOperationException("El rol a duplicar no existe.");

            var copia = (Rol)original.Clonar();
            copia.Nombre = nuevoNombre;
            copia.Protegido = false;

            int id = await _roles.InsertarRolAsync(copia.Nombre, copia.PadreId);
            await _roles.GuardarPermisosDeRolAsync(id, copia.ObtenerHijos().Where(h => h.EsHoja()).Select(h => h.Id));
            await _bitacora.RegistrarAsync(actor, "ROL_DUPLICADO:" + nuevoNombre);
            return id;
        }
        finally { _cambios.Release(); }
    }

    public async Task CambiarPadreAsync(string actor, int rolId, int? nuevoPadreId)
    {
        await _cambios.WaitAsync();
        try
        {
            var todos = await _roles.ListarRolesAsync();
            var rol = todos.FirstOrDefault(r => r.Id == rolId) ?? throw new InvalidOperationException("El rol no existe.");

            if (nuevoPadreId.HasValue)
            {
                if (todos.All(r => r.Id != nuevoPadreId.Value))
                    throw new InvalidOperationException("El rol padre no existe.");
                if (ReglasRoles.GeneraCiclo(rolId, nuevoPadreId.Value, todos))
                    throw new InvalidOperationException("No se puede asignar ese padre: generaría una referencia circular.");
                if (ReglasRoles.EsAncestroDelPadreActual(rol.PadreId, nuevoPadreId.Value, todos))
                    throw new InvalidOperationException("No se puede asignar como padre a un ancestro del padre actual del rol.");
            }

            await _roles.CambiarPadreAsync(rolId, nuevoPadreId);
            await _bitacora.RegistrarAsync(actor, "ROL_PADRE_CAMBIADO:" + rol.Nombre);
        }
        finally { _cambios.Release(); }
    }

    public async Task EliminarAsync(string actor, int rolId)
    {
        await _cambios.WaitAsync();
        try
        {
            var todos = await _roles.ListarRolesAsync();
            var rol = todos.FirstOrDefault(r => r.Id == rolId) ?? throw new InvalidOperationException("El rol no existe.");

            if (rol.Protegido)
                throw new InvalidOperationException("Este rol es del sistema y no puede eliminarse.");

            // Se elimina el rol con todos sus descendientes: ninguno puede tener usuarios
            var subarbol = ReglasRoles.SubarbolDe(rolId, todos);
            var asignaciones = await _roles.ListarAsignacionesAsync();
            if (asignaciones.Values.Any(rolesDeUsuario => rolesDeUsuario.Any(subarbol.Contains)))
                throw new InvalidOperationException("No se puede eliminar un rol que tiene usuarios asignados. Reasignálos primero.");

            await _roles.EliminarRolesAsync(subarbol);
            await _bitacora.RegistrarAsync(actor, "ROL_ELIMINADO:" + rol.Nombre);
        }
        finally { _cambios.Release(); }
    }

    public async Task ActualizarPermisosAsync(string actor, int rolId, List<int> permisoIds)
    {
        await _cambios.WaitAsync();
        try
        {
            var rol = (await _roles.ListarRolesAsync()).FirstOrDefault(r => r.Id == rolId)
                ?? throw new InvalidOperationException("El rol no existe.");

            var catalogo = (await _roles.ListarPermisosAsync()).Select(p => p.Id).ToHashSet();
            if ((permisoIds ?? new List<int>()).Any(p => !catalogo.Contains(p)))
                throw new InvalidOperationException("Alguno de los permisos indicados no existe.");

            permisoIds = permisoIds?.Distinct().ToList() ?? new List<int>();
            await _guarda.ValidarAsync(rolId: rolId, nuevosPermisoIds: permisoIds);

            await _roles.GuardarPermisosDeRolAsync(rolId, permisoIds);
            await _bitacora.RegistrarAsync(actor, "ROL_PERMISOS_MODIFICADOS:" + rol.Nombre);
        }
        finally { _cambios.Release(); }
    }

    private static string ValidarNombre(string? nombre, IEnumerable<Rol> existentes)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) throw new InvalidOperationException("Indicá el nombre del rol.");
        if (nombre.Length > LargoMaximoNombre)
            throw new InvalidOperationException($"El nombre no puede superar los {LargoMaximoNombre} caracteres.");
        if (existentes.Any(r => r.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe un rol con ese nombre.");
        return nombre;
    }

    private static NodoRolDto AUno(NodoPermiso n) => new()
    {
        Id = n.Id,
        Nombre = n.Nombre,
        PadreId = n.PadreId,
        EsRol = !n.EsHoja(),
        Protegido = n is Rol r && r.Protegido,
        Hijos = n.ObtenerHijos().Select(AUno).ToList()
    };
}
