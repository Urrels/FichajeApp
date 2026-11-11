using Microsoft.EntityFrameworkCore;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Persistencia;

// Al arrancar con base de datos SQL: aplica las migraciones pendientes y siembra lo que la administración necesita
// (permisos, roles iniciales, parámetros y el primer administrador). Es idempotente: no toca lo que ya existe.
//
// El primer administrador sale de la configuración (variables de entorno Setup__AdminNombre, Setup__AdminEmail y
// Setup__AdminPin) y solo se crea si todavía no hay ningún empleado.
public class InicializadorSql : IHostedService
{
    private readonly IDbContextFactory<FichajeDbContext> _fabrica;
    private readonly IRolesRepositorio _roles;
    private readonly IEmpleadosRepositorio _empleados;
    private readonly PermisosService _permisos;
    private readonly IConfiguration _config;
    private readonly ILogger<InicializadorSql> _log;

    public InicializadorSql(IDbContextFactory<FichajeDbContext> fabrica, IRolesRepositorio roles, IEmpleadosRepositorio empleados,
        PermisosService permisos, IConfiguration config, ILogger<InicializadorSql> log)
    {
        _fabrica = fabrica;
        _roles = roles;
        _empleados = empleados;
        _permisos = permisos;
        _config = config;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InicializarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // La API sigue arriba (para que se vea el error en el registro y en /api/apariencia); las consultas fallarán hasta arreglar la conexión
            _log.LogError(ex, "No se pudo inicializar la base de datos. Revisá la cadena de conexión (ConnectionStrings__Fichaje).");
        }
    }

    public async Task InicializarAsync(CancellationToken cancelar = default)
    {
        if (_config.GetValue("Datos:Migrar", true))
        {
            await using var db = await _fabrica.CreateDbContextAsync(cancelar);
            // En una base de Azure SQL en pausa la primera conexión tarda: la estrategia de reintentos de EF espera sola
            await db.Database.MigrateAsync(cancelar);
        }

        await RetirarPermisosObsoletosAsync();
        await SembrarPermisosAsync();
        await SembrarRolesAsync();
        await AsegurarPermisosDelAdministradorAsync();
        await SembrarParametrosAsync();
        await CrearPrimerAdministradorAsync();
        await MigrarAsignacionesAsync();
        await GarantizarAdministradorAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Borra los permisos que ya no existen en el catálogo (con sus asignaciones a roles), por ejemplo el de cambiar la
    // catálogo: si no, seguirían apareciendo en la pantalla de roles sin hacer nada.
    private async Task RetirarPermisosObsoletosAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var ids = await db.Permisos.Where(p => CatalogoPermisos.Obsoletos.Contains(p.Nombre)).Select(p => p.Id).ToListAsync();
        if (ids.Count == 0) return;

        db.RolPermisos.RemoveRange(db.RolPermisos.Where(rp => ids.Contains(rp.PermisoId)));
        db.Permisos.RemoveRange(db.Permisos.Where(p => ids.Contains(p.Id)));
        await db.SaveChangesAsync();
        _log.LogInformation("Se retiraron {Cantidad} permisos que ya no se usan.", ids.Count);
    }

    private async Task SembrarPermisosAsync()
    {
        var existentes = (await _roles.ListarPermisosAsync()).Select(p => p.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var nombre in CatalogoPermisos.Todos.Where(n => !existentes.Contains(n)))
            await _roles.InsertarPermisoAsync(nombre);
    }

    private async Task SembrarRolesAsync()
    {
        if ((await _roles.ListarRolesAsync()).Count > 0) return;

        var permisos = (await _roles.ListarPermisosAsync()).ToDictionary(p => p.Nombre, p => p.Id);
        foreach (var (nombre, permisosDelRol) in CatalogoPermisos.RolesIniciales)
        {
            int id = await _roles.InsertarRolAsync(nombre, null, protegido: nombre == CatalogoPermisos.RolAdministrador);
            await _roles.GuardarPermisosDeRolAsync(id, permisosDelRol.Select(p => permisos[p]));
        }
    }

    // El rol Administrador (protegido) tiene todos los permisos por definición: cuando una versión nueva suma uno, se le asigna solo
    private async Task AsegurarPermisosDelAdministradorAsync()
    {
        var admin = (await _roles.ListarRolesAsync()).FirstOrDefault(r => r.Protegido && r.Nombre == CatalogoPermisos.RolAdministrador);
        if (admin is null) return;

        var catalogo = (await _roles.ListarPermisosAsync()).Select(p => p.Id).ToHashSet();
        var actuales = (await _roles.ListarRolPermisosAsync()).Where(rp => rp.RolId == admin.Id).Select(rp => rp.PermisoId).ToHashSet();
        if (catalogo.IsSubsetOf(actuales)) return;

        await _roles.GuardarPermisosDeRolAsync(admin.Id, catalogo);
        _log.LogInformation("El rol Administrador recibió los permisos nuevos del catálogo.");
    }

    private async Task SembrarParametrosAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        if (await db.Parametros.AnyAsync(p => p.Id == 1)) return;
        db.Parametros.Add(new ParametrosFila());
        await db.SaveChangesAsync();
    }

    private async Task CrearPrimerAdministradorAsync()
    {
        var email = _config["Setup:AdminEmail"];
        var nombre = _config["Setup:AdminNombre"];
        var pin = _config["Setup:AdminPin"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(pin)) return;
        if ((await _empleados.ListarAsync()).Count > 0) return;

        var errorPin = ValidadorPin.ObtenerError(pin);
        if (errorPin is not null)
        {
            _log.LogError("Setup__AdminPin no sirve: {Error}", errorPin);
            return;
        }

        await _empleados.CrearAsync(new Models.EmpleadoDto
        {
            NombreCompleto = nombre.Trim(),
            Email = email.Trim(),
            PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
            Activo = true,
            Rol = CatalogoPermisos.RolAdministrador
        });
        _log.LogInformation("Se creó el primer administrador: {Email}", email);
    }

    // Cada empleado queda con el rol indicado al crearlo (Administrador / Supervisor / Empleado) la primera vez
    private async Task MigrarAsignacionesAsync()
    {
        if ((await _roles.ListarAsignacionesAsync()).Count > 0) return;

        var roles = await _roles.ListarRolesAsync();
        foreach (var e in await _empleados.ListarAsync())
        {
            var ids = PermisosService.RolesEfectivos(e, new Dictionary<int, List<int>>(), roles);
            if (ids.Count > 0) await _roles.ReemplazarAsignacionesAsync(e.Id, ids);
        }
    }

    // Si nadie puede administrar, se promueve a Setup:AdminEmail
    private async Task GarantizarAdministradorAsync()
    {
        var usuarios = await _permisos.RolesDeUsuariosActivosAsync();
        var permisosPorRol = await _permisos.PermisosPorRolAsync();
        bool hayAdministrador = usuarios.Values.Any(roles => roles.Any(r =>
            permisosPorRol.TryGetValue(r, out var p) && CatalogoPermisos.DeAdministracion.All(p.Contains)));
        if (hayAdministrador) return;

        var email = _config["Setup:AdminEmail"];
        if (string.IsNullOrWhiteSpace(email))
        {
            _log.LogWarning("Ningún usuario activo puede administrar el sistema. Definí Setup__AdminNombre, Setup__AdminEmail y Setup__AdminPin " +
                            "para crear el primer administrador.");
            return;
        }

        var empleado = await _empleados.BuscarPorEmailAsync(email);
        var admin = (await _roles.ListarRolesAsync()).FirstOrDefault(r => r.Nombre == CatalogoPermisos.RolAdministrador);
        if (empleado is null || admin is null)
        {
            _log.LogWarning("{Email} no corresponde a un empleado activo: no se pudo nombrar administrador.", email);
            return;
        }

        var actuales = (await _roles.ListarAsignacionesAsync()).GetValueOrDefault(empleado.Id) ?? new List<int>();
        await _roles.ReemplazarAsignacionesAsync(empleado.Id, actuales.Append(admin.Id));
        _log.LogWarning("{Email} quedó como Administrador porque no había ningún administrador activo.", email);
    }
}

