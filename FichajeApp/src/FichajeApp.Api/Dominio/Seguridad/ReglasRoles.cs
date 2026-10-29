namespace FichajeApp.Api.Dominio.Seguridad;

// Reglas puras sobre roles y permisos (sin acceso a datos, se prueban con listas en memoria).
// Portadas de PerfilBLL de ingenieria_software.
public static class ReglasRoles
{
    // Arma el árbol de roles: los roles cuelgan de su padre (PadreId) y cada permiso del rol que lo tiene.
    // PadreId es solo organizativo: NO hay herencia de permisos (en el original causó una escalada de privilegios).
    public static List<NodoPermiso> ArmarArbol(IEnumerable<Rol> roles, IEnumerable<(int RolId, Permiso Permiso)> rolPermisos)
    {
        var lista = roles.ToList();
        var mapa = lista.ToDictionary(r => r.Id);

        var raices = new List<NodoPermiso>();
        foreach (var rol in lista)
        {
            if (rol.PadreId is null)
                raices.Add(rol);
            else if (mapa.TryGetValue(rol.PadreId.Value, out var padre))
                padre.Agregar(rol);
        }

        foreach (var (rolId, permiso) in rolPermisos)
        {
            if (mapa.TryGetValue(rolId, out var rol))
                rol.Agregar(permiso);
        }

        return raices;
    }

    public static Rol? BuscarRol(IEnumerable<NodoPermiso> nodos, int rolId)
    {
        foreach (var nodo in nodos)
        {
            if (nodo is not Rol rol) continue;
            if (rol.Id == rolId) return rol;
            var encontrado = BuscarRol(rol.ObtenerHijos(), rolId);
            if (encontrado != null) return encontrado;
        }
        return null;
    }

    // true si asignar candidatoPadreId como padre de rolId crearía un ciclo
    // (el candidato es el propio rol o un descendiente suyo)
    public static bool GeneraCiclo(int rolId, int candidatoPadreId, IReadOnlyCollection<Rol> todos)
    {
        int? actual = candidatoPadreId;
        var visitados = new HashSet<int>();
        while (actual.HasValue)
        {
            if (actual.Value == rolId) return true;
            if (!visitados.Add(actual.Value)) return true; // datos ya corruptos: no seguir
            actual = todos.FirstOrDefault(n => n.Id == actual.Value)?.PadreId;
        }
        return false;
    }

    // true si el candidato es un ancestro por encima del padre actual (solo se permiten movimientos laterales)
    public static bool EsAncestroDelPadreActual(int? padreActualId, int candidatoPadreId, IReadOnlyCollection<Rol> todos)
    {
        int? actual = padreActualId.HasValue
            ? todos.FirstOrDefault(n => n.Id == padreActualId.Value)?.PadreId
            : null;
        var visitados = new HashSet<int>();
        while (actual.HasValue)
        {
            if (actual.Value == candidatoPadreId) return true;
            if (!visitados.Add(actual.Value)) return false;
            actual = todos.FirstOrDefault(n => n.Id == actual.Value)?.PadreId;
        }
        return false;
    }

    // Ids del rol y de todos sus descendientes (para eliminar en cascada)
    public static List<int> SubarbolDe(int rolId, IReadOnlyCollection<Rol> todos)
    {
        var resultado = new List<int> { rolId };
        for (int i = 0; i < resultado.Count; i++)
            resultado.AddRange(todos.Where(r => r.PadreId == resultado[i] && !resultado.Contains(r.Id)).Select(r => r.Id));
        return resultado;
    }

    // Guarda anti-lockout: simula el cambio en memoria y devuelve el permiso de administración que
    // dejaría de estar cubierto por algún usuario activo (o null si todo sigue cubierto).
    public static string? PermisoQueSePerderia(
        IReadOnlyDictionary<int, List<int>> rolesPorUsuarioActivo,
        IReadOnlyDictionary<int, HashSet<string>> permisosPorRol,
        int? rolId = null, HashSet<string>? nuevosPermisos = null,
        int? usuarioId = null, List<int>? nuevosRolIds = null)
    {
        var cubiertosAntes = PermisosCubiertos(rolesPorUsuarioActivo, permisosPorRol);

        var permisosDespues = permisosPorRol.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (rolId.HasValue && nuevosPermisos != null)
            permisosDespues[rolId.Value] = nuevosPermisos;

        var rolesDespues = rolesPorUsuarioActivo.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (usuarioId.HasValue && nuevosRolIds != null)
        {
            // Un usuario dado de baja o bloqueado se simula con una lista vacía de roles
            if (rolesDespues.ContainsKey(usuarioId.Value))
                rolesDespues[usuarioId.Value] = nuevosRolIds;
        }

        var cubiertosDespues = PermisosCubiertos(rolesDespues, permisosDespues);
        return cubiertosAntes.FirstOrDefault(p => !cubiertosDespues.Contains(p));
    }

    private static HashSet<string> PermisosCubiertos(
        IReadOnlyDictionary<int, List<int>> rolesPorUsuario,
        IReadOnlyDictionary<int, HashSet<string>> permisosPorRol)
    {
        var cubiertos = new HashSet<string>();
        foreach (var roles in rolesPorUsuario.Values)
            foreach (var rol in roles)
                if (permisosPorRol.TryGetValue(rol, out var permisos))
                    foreach (var permiso in permisos)
                        if (CatalogoPermisos.DeAdministracion.Contains(permiso))
                            cubiertos.Add(permiso);
        return cubiertos;
    }
}
