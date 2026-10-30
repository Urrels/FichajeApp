using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// El rol de las cuentas de sucursal. No existe hasta que el administrador activa el modo kiosco.
public static class RolKiosco
{
    // Crea el rol con su permiso si todavía no está; devuelve true si lo creó. Se puede llamar las veces que haga falta.
    public static async Task<bool> AsegurarAsync(IRolesRepositorio roles)
    {
        if ((await roles.ListarRolesAsync()).Any(r => r.Nombre == CatalogoPermisos.RolKiosco)) return false;

        var permisos = (await roles.ListarPermisosAsync()).ToDictionary(p => p.Nombre, p => p.Id);
        var ids = CatalogoPermisos.PermisosDelRolKiosco.Where(permisos.ContainsKey).Select(n => permisos[n]).ToList();
        if (ids.Count == 0) return false; // el catálogo todavía no se sembró

        int rolId = await roles.InsertarRolAsync(CatalogoPermisos.RolKiosco, null, protegido: false);
        await roles.GuardarPermisosDeRolAsync(rolId, ids);
        return true;
    }
}
