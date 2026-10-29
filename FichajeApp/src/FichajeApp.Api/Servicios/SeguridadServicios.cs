using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

public static class ValidadorPin
{
    public const int Largo = 4;

    // El PIN de FichajeApp es numérico de 4 dígitos (la app lo pide con teclado numérico)
    public static string? ObtenerError(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length != Largo || !pin.All(char.IsDigit))
            return $"El PIN debe tener exactamente {Largo} dígitos.";
        return null;
    }
}

// Bitácora de acciones de seguridad y administración
public class BitacoraService
{
    private const int LargoMaximoAccion = 100;

    private readonly IBitacoraRepositorio _repositorio;
    private readonly TimeProvider _tiempo;

    public BitacoraService(IBitacoraRepositorio repositorio, TimeProvider tiempo)
    {
        _repositorio = repositorio;
        _tiempo = tiempo;
    }

    public Task RegistrarAsync(string? usuario, string accion)
    {
        if (accion.Length > LargoMaximoAccion) accion = accion[..LargoMaximoAccion];
        return _repositorio.RegistrarAsync(new EntradaBitacora
        {
            FechaHora = _tiempo.GetLocalNow().DateTime,
            Usuario = string.IsNullOrWhiteSpace(usuario) ? "sistema" : usuario,
            Accion = accion
        });
    }

    public async Task<PaginaResultado<EntradaBitacora>> ListarAsync(
        string? usuario, string? accion, DateTime? desde, DateTime? hasta, int pagina, int tamanio)
    {
        pagina = Math.Max(1, pagina);
        tamanio = Math.Clamp(tamanio, 1, 200);

        var datos = (await _repositorio.ListarAsync()).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(usuario))
            datos = datos.Where(e => e.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(accion))
            datos = datos.Where(e => e.Accion.Contains(accion, StringComparison.OrdinalIgnoreCase));
        if (desde.HasValue) datos = datos.Where(e => e.FechaHora >= desde.Value.Date);
        if (hasta.HasValue) datos = datos.Where(e => e.FechaHora < hasta.Value.Date.AddDays(1));

        var lista = datos.OrderByDescending(e => e.FechaHora).ToList();
        return new PaginaResultado<EntradaBitacora>
        {
            Total = lista.Count,
            Pagina = pagina,
            Tamanio = tamanio,
            Items = lista.Skip((pagina - 1) * tamanio).Take(tamanio).ToList()
        };
    }
}

// Roles y permisos efectivos de cada usuario.
// Los roles asignados (hoja UsuarioRol) mandan; quien nunca tuvo asignación usa el rol inicial de la
// columna F de Empleados ("Administrador", "Supervisor" o, en cualquier otro caso, "Empleado").
// No hay herencia de permisos entre roles: un rol tiene exactamente los permisos que tiene asignados.
public class PermisosService
{
    private readonly IRolesRepositorio _roles;
    private readonly IEmpleadosRepositorio _empleados;

    public PermisosService(IRolesRepositorio roles, IEmpleadosRepositorio empleados)
    {
        _roles = roles;
        _empleados = empleados;
    }

    public static List<int> RolesEfectivos(EmpleadoDto empleado, IReadOnlyDictionary<int, List<int>> asignaciones, IReadOnlyCollection<Rol> roles)
    {
        if (asignaciones.TryGetValue(empleado.Id, out var asignados) && asignados.Count > 0)
            return asignados;

        string nombre = empleado.Rol.Equals(CatalogoPermisos.RolAdministrador, StringComparison.OrdinalIgnoreCase) ? CatalogoPermisos.RolAdministrador
            : empleado.Rol.Equals(CatalogoPermisos.RolSupervisor, StringComparison.OrdinalIgnoreCase) ? CatalogoPermisos.RolSupervisor
            : CatalogoPermisos.RolEmpleado;

        var rol = roles.FirstOrDefault(r => r.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        return rol is null ? new List<int>() : new List<int> { rol.Id };
    }

    public async Task<(List<string> Roles, List<string> Permisos)> ObtenerAsync(EmpleadoDto empleado)
    {
        var roles = await _roles.ListarRolesAsync();
        var asignaciones = await _roles.ListarAsignacionesAsync();
        var permisosPorRol = await PermisosPorRolAsync();

        var ids = RolesEfectivos(empleado, asignaciones, roles);
        var nombresRoles = roles.Where(r => ids.Contains(r.Id)).Select(r => r.Nombre).OrderBy(n => n).ToList();
        var permisos = ids.Where(permisosPorRol.ContainsKey).SelectMany(id => permisosPorRol[id]).Distinct().OrderBy(p => p).ToList();
        return (nombresRoles, permisos);
    }

    public async Task<Dictionary<int, HashSet<string>>> PermisosPorRolAsync()
    {
        var permisos = (await _roles.ListarPermisosAsync()).ToDictionary(p => p.Id, p => p.Nombre);
        var resultado = new Dictionary<int, HashSet<string>>();
        foreach (var (rolId, permisoId) in await _roles.ListarRolPermisosAsync())
        {
            if (!permisos.TryGetValue(permisoId, out var nombre)) continue;
            if (!resultado.TryGetValue(rolId, out var set)) resultado[rolId] = set = new HashSet<string>();
            set.Add(nombre);
        }
        return resultado;
    }

    // Roles de los usuarios que hoy pueden entrar: activos y no bloqueados
    public async Task<Dictionary<int, List<int>>> RolesDeUsuariosActivosAsync()
    {
        var roles = await _roles.ListarRolesAsync();
        var asignaciones = await _roles.ListarAsignacionesAsync();
        return (await _empleados.ListarAsync())
            .Where(e => e.Activo && !e.Bloqueado)
            .ToDictionary(e => e.Id, e => RolesEfectivos(e, asignaciones, roles));
    }
}

// Guarda anti-lockout: ningún cambio puede dejar al sistema sin un usuario activo con cada permiso de administración
public class GuardaAdministracion
{
    private readonly IRolesRepositorio _roles;
    private readonly PermisosService _permisos;

    public GuardaAdministracion(IRolesRepositorio roles, PermisosService permisos)
    {
        _roles = roles;
        _permisos = permisos;
    }

    public async Task ValidarAsync(int? rolId = null, List<int>? nuevosPermisoIds = null,
                                   int? usuarioId = null, List<int>? nuevosRolIds = null)
    {
        var usuarios = await _permisos.RolesDeUsuariosActivosAsync();
        var permisosPorRol = await _permisos.PermisosPorRolAsync();

        HashSet<string>? nuevosPermisos = null;
        if (rolId.HasValue && nuevosPermisoIds != null)
        {
            nuevosPermisos = (await _roles.ListarPermisosAsync())
                .Where(p => nuevosPermisoIds.Contains(p.Id)).Select(p => p.Nombre).ToHashSet();
        }

        var perdido = ReglasRoles.PermisoQueSePerderia(usuarios, permisosPorRol, rolId, nuevosPermisos, usuarioId, nuevosRolIds);
        if (perdido != null)
            throw new InvalidOperationException(
                $"No se puede guardar: el sistema quedaría sin ningún usuario activo con el permiso '{perdido}'.");
    }
}

// Inicio de sesión con bloqueo por intentos fallidos (3), como en ingenieria_software.
// Se mantiene BCrypt para el PIN (el original usaba SHA-256 sin sal, más débil).
public class LoginService
{
    public const int MaxIntentos = 3;

    private readonly IEmpleadosRepositorio _empleados;
    private readonly PermisosService _permisos;
    private readonly BitacoraService _bitacora;
    private readonly HistorialUsuariosService _historial;

    public LoginService(IEmpleadosRepositorio empleados, PermisosService permisos, BitacoraService bitacora, HistorialUsuariosService historial)
    {
        _empleados = empleados;
        _permisos = permisos;
        _bitacora = bitacora;
        _historial = historial;
    }

    public async Task<ResultadoLogin> AutenticarAsync(string? email, string? pin)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(pin))
            return new ResultadoLogin { Resultado = LoginResultado.CredencialesInvalidas };

        return await VerificarAsync(await _empleados.BuscarPorEmailAsync(email, incluirInactivos: true), pin);
    }

    // En el kiosco el empleado elige su nombre en la lista: mismo control del PIN y mismo bloqueo por intentos,
    // pero sin dejar un "LOGIN" en la bitácora por cada fichaje
    public async Task<ResultadoLogin> AutenticarPorIdAsync(int empleadoId, string? pin)
    {
        if (string.IsNullOrEmpty(pin))
            return new ResultadoLogin { Resultado = LoginResultado.CredencialesInvalidas };

        return await VerificarAsync(await _empleados.ObtenerPorIdAsync(empleadoId), pin, registrarLogin: false);
    }

    private async Task<ResultadoLogin> VerificarAsync(EmpleadoDto? empleado, string pin, bool registrarLogin = true)
    {
        // No se distingue "no existe" de "dado de baja": mismo mensaje y sin contar intentos
        if (empleado is null || !empleado.Activo)
            return new ResultadoLogin { Resultado = LoginResultado.CredencialesInvalidas };

        if (empleado.Bloqueado)
            return new ResultadoLogin { Resultado = LoginResultado.UsuarioBloqueado };

        if (BCrypt.Net.BCrypt.Verify(pin, empleado.PinHash))
        {
            if (empleado.IntentosFallidos > 0)
            {
                empleado.IntentosFallidos = 0;
                await _empleados.ActualizarAsync(empleado);
            }

            var (roles, permisos) = await _permisos.ObtenerAsync(empleado);
            if (registrarLogin) await _bitacora.RegistrarAsync(empleado.Email, "LOGIN");
            return new ResultadoLogin { Resultado = LoginResultado.Exito, Empleado = empleado, Roles = roles, Permisos = permisos };
        }

        empleado.IntentosFallidos++;
        bool bloquear = empleado.IntentosFallidos >= MaxIntentos;
        if (bloquear) empleado.Bloqueado = true;
        await _empleados.ActualizarAsync(empleado);

        await _bitacora.RegistrarAsync(empleado.Email, "LOGIN_FALLIDO");
        if (bloquear)
        {
            await _bitacora.RegistrarAsync(empleado.Email, "USUARIO_BLOQUEADO");
            await _historial.RegistrarAsync(empleado, "BLOQUEO", "sistema");
            return new ResultadoLogin { Resultado = LoginResultado.UsuarioBloqueado };
        }

        return new ResultadoLogin { Resultado = LoginResultado.CredencialesInvalidas };
    }
}
