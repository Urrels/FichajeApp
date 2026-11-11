using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Jerarquía de la empresa, en tres niveles:
//   Administrador (permiso "Administrar usuarios")  -> ve y gestiona a todos: supervisores y empleados.
//   Supervisor    (permiso "Ver panel de supervisor") -> ve y gestiona SOLO a los empleados que tiene asignados.
//   Empleado                                          -> ficha; no ve a nadie más.
// La relación es EmpleadoDto.SupervisorId (un solo nivel: un supervisor depende del administrador, no de otro supervisor).

// A quién puede ver y gestionar quien hace un pedido
public sealed class AlcanceDeUsuario
{
    private readonly HashSet<string> _nombres;

    private AlcanceDeUsuario(bool global, IEnumerable<string> nombres)
    {
        EsGlobal = global;
        _nombres = new HashSet<string>(nombres, StringComparer.OrdinalIgnoreCase);
    }

    // true = administrador (o un servidor sin jerarquía): ve a todos
    public bool EsGlobal { get; }

    public IReadOnlyCollection<string> Nombres => _nombres;

    public bool Incluye(string? nombre) => EsGlobal || (nombre is not null && _nombres.Contains(nombre));

    public static AlcanceDeUsuario Global() => new(true, Array.Empty<string>());
    public static AlcanceDeUsuario DeNombres(IEnumerable<string> nombres) => new(false, nombres);
    public static AlcanceDeUsuario Nadie() => new(false, Array.Empty<string>());
}

// Quién se entera de lo que hace un empleado
public sealed record Destinatarios(EmpleadoDto? Supervisor, IReadOnlyList<EmpleadoDto> Administradores)
{
    // Sin supervisor válido: el aviso llega directo a los administradores
    public bool SinSupervisor => Supervisor is null;
}

public class JerarquiaService
{
    private readonly IEmpleadosRepositorio _empleados;
    private readonly PermisosService _permisos;

    public JerarquiaService(IEmpleadosRepositorio empleados, PermisosService permisos)
    {
        _empleados = empleados;
        _permisos = permisos;
    }

    // Las personas activas (y no bloqueadas) que hoy tienen un permiso, con una sola lectura de roles
    public async Task<HashSet<int>> PersonasConPermisoAsync(string permiso)
    {
        var rolesDe = await _permisos.RolesDeUsuariosActivosAsync();
        var permisosPorRol = await _permisos.PermisosPorRolAsync();
        return rolesDe
            .Where(kv => kv.Value.Any(rolId => permisosPorRol.TryGetValue(rolId, out var p) && p.Contains(permiso)))
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    // Lo que puede ver quien tiene este email: el administrador, todo; el supervisor, sus empleados (y a sí mismo)
    public async Task<AlcanceDeUsuario> AlcanceDeAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return AlcanceDeUsuario.Nadie();
        var persona = await _empleados.BuscarPorEmailAsync(email);
        if (persona is null) return AlcanceDeUsuario.Nadie();

        var (_, permisos) = await _permisos.ObtenerAsync(persona);
        if (permisos.Contains(CatalogoPermisos.AdministrarUsuarios)) return AlcanceDeUsuario.Global();

        var nombres = (await _empleados.ListarAsync())
            .Where(e => e.SupervisorId == persona.Id)
            .Select(e => e.NombreCompleto)
            .Append(persona.NombreCompleto);
        return AlcanceDeUsuario.DeNombres(nombres);
    }

    // El supervisor directo de esta persona, solo si hoy puede actuar: activo, no bloqueado y con permiso de supervisor.
    // Si fue dado de baja o perdió el permiso, la persona queda "sin supervisor" y los avisos van al administrador.
    public async Task<Destinatarios> DestinatariosDeAsync(EmpleadoDto autor)
    {
        var todos = await _empleados.ListarAsync();
        var conPanel = await PersonasConPermisoAsync(CatalogoPermisos.VerPanelSupervisor);
        var admins = await PersonasConPermisoAsync(CatalogoPermisos.AdministrarUsuarios);

        EmpleadoDto? supervisor = null;
        if (autor.SupervisorId is int sid && sid != autor.Id)
            supervisor = todos.FirstOrDefault(e => e.Id == sid && e.Activo && !e.Bloqueado && conPanel.Contains(e.Id));

        var administradores = todos.Where(e => admins.Contains(e.Id) && e.Id != autor.Id).ToList();
        return new Destinatarios(supervisor, administradores);
    }

    // Candidatos para "supervisor a cargo": con permiso de supervisor y que no sean administradores (esos ya lo ven todo)
    public async Task<List<EmpleadoResumenSupervisor>> SupervisoresDisponiblesAsync()
    {
        var conPanel = await PersonasConPermisoAsync(CatalogoPermisos.VerPanelSupervisor);
        var admins = await PersonasConPermisoAsync(CatalogoPermisos.AdministrarUsuarios);
        return (await _empleados.ListarAsync())
            .Where(e => e.Activo && conPanel.Contains(e.Id) && !admins.Contains(e.Id))
            .OrderBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EmpleadoResumenSupervisor(e.Id, e.NombreCompleto))
            .ToList();
    }
}

public record EmpleadoResumenSupervisor(int Id, string Nombre);
