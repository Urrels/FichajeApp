using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Historial de cambios de usuarios (solo inserción): cada fila es una foto completa del estado.
public class HistorialUsuariosService
{
    private readonly IHistorialUsuariosRepositorio _repositorio;
    private readonly IRolesRepositorio _roles;
    private readonly TimeProvider _tiempo;

    public HistorialUsuariosService(IHistorialUsuariosRepositorio repositorio, IRolesRepositorio roles, TimeProvider tiempo)
    {
        _repositorio = repositorio;
        _roles = roles;
        _tiempo = tiempo;
    }

    public async Task<int> RegistrarAsync(EmpleadoDto e, string tipo, string realizadoPor, int? versionOrigen = null)
    {
        var rolesDefinidos = await _roles.ListarRolesAsync();
        var asignaciones = await _roles.ListarAsignacionesAsync();

        return await _repositorio.RegistrarAsync(new HistorialUsuario
        {
            FechaHora = _tiempo.GetLocalNow().DateTime,
            EmpleadoId = e.Id,
            Tipo = tipo,
            RealizadoPor = realizadoPor,
            Nombre = e.NombreCompleto,
            Email = e.Email,
            Activo = e.Activo,
            Bloqueado = e.Bloqueado,
            IntentosFallidos = e.IntentosFallidos,
            RolIds = PermisosService.RolesEfectivos(e, asignaciones, rolesDefinidos),
            VersionOrigen = versionOrigen
        });
    }

    public Task<List<HistorialUsuario>> ListarAsync(int empleadoId) => _repositorio.ListarAsync(empleadoId);
}

// Administración de usuarios. Un usuario no se borra: se da de baja (Activo = false), porque sus
// fichajes y liquidaciones son datos de pago que tienen que seguir existiendo.
public class UsuariosAdminService
{
    private readonly IEmpleadosRepositorio _empleados;
    private readonly IRolesRepositorio _roles;
    private readonly IFichajesRepositorio _fichajes;
    private readonly HistorialUsuariosService _historial;
    private readonly IHistorialUsuariosRepositorio _historialRepositorio;
    private readonly GuardaAdministracion _guarda;
    private readonly BitacoraService _bitacora;

    public UsuariosAdminService(IEmpleadosRepositorio empleados, IRolesRepositorio roles, IFichajesRepositorio fichajes,
        HistorialUsuariosService historial, IHistorialUsuariosRepositorio historialRepositorio,
        GuardaAdministracion guarda, BitacoraService bitacora)
    {
        _empleados = empleados;
        _roles = roles;
        _fichajes = fichajes;
        _historial = historial;
        _historialRepositorio = historialRepositorio;
        _guarda = guarda;
        _bitacora = bitacora;
    }

    public async Task<PaginaResultado<UsuarioAdminDto>> ListarAsync(string? busqueda, int pagina, int tamanio)
    {
        pagina = Math.Max(1, pagina);
        tamanio = Math.Clamp(tamanio, 1, 100);

        var roles = await _roles.ListarRolesAsync();
        var asignaciones = await _roles.ListarAsignacionesAsync();

        var empleados = (await _empleados.ListarAsync()).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(busqueda))
            empleados = empleados.Where(e => e.NombreCompleto.Contains(busqueda, StringComparison.OrdinalIgnoreCase)
                                          || e.Email.Contains(busqueda, StringComparison.OrdinalIgnoreCase));

        var lista = empleados.OrderBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase).ToList();
        return new PaginaResultado<UsuarioAdminDto>
        {
            Total = lista.Count,
            Pagina = pagina,
            Tamanio = tamanio,
            Items = lista.Skip((pagina - 1) * tamanio).Take(tamanio).Select(e =>
            {
                var ids = PermisosService.RolesEfectivos(e, asignaciones, roles);
                return new UsuarioAdminDto
                {
                    Id = e.Id,
                    NombreCompleto = e.NombreCompleto,
                    Email = e.Email,
                    Activo = e.Activo,
                    Bloqueado = e.Bloqueado,
                    IntentosFallidos = e.IntentosFallidos,
                    RolIds = ids,
                    Roles = roles.Where(r => ids.Contains(r.Id)).Select(r => r.Nombre).ToList()
                };
            }).ToList()
        };
    }

    public async Task<int> CrearAsync(string actor, string nombre, string email, string pin, List<int> rolIds)
    {
        nombre = (nombre ?? "").Trim();
        email = (email ?? "").Trim();

        if (nombre.Length == 0) throw new InvalidOperationException("Indicá el nombre completo.");
        ValidarEmail(email);
        var errorPin = ValidadorPin.ObtenerError(pin);
        if (errorPin != null) throw new InvalidOperationException(errorPin);

        var existentes = await _empleados.ListarAsync();
        if (existentes.Any(e => e.NombreCompleto.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe un empleado con ese nombre (los fichajes se identifican por nombre).");
        if (existentes.Any(e => e.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe un empleado con ese email.");

        await ValidarRolesAsync(rolIds);

        var nuevo = await _empleados.CrearAsync(new EmpleadoDto
        {
            NombreCompleto = nombre,
            Email = email,
            PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
            Activo = true,
            Rol = "Empleado"
        });
        await _roles.ReemplazarAsignacionesAsync(nuevo.Id, rolIds);

        await _historial.RegistrarAsync(nuevo, "ALTA", actor);
        await _bitacora.RegistrarAsync(actor, "USUARIO_CREADO:" + nombre);
        return nuevo.Id;
    }

    public async Task ActualizarDatosAsync(string actor, int id, string nombre, string email)
    {
        var e = await ObtenerAsync(id);
        nombre = (nombre ?? "").Trim();
        email = (email ?? "").Trim();

        if (nombre.Length == 0) throw new InvalidOperationException("Indicá el nombre completo.");
        ValidarEmail(email);

        var otros = (await _empleados.ListarAsync()).Where(x => x.Id != id).ToList();
        if (otros.Any(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe un empleado con ese email.");

        if (!nombre.Equals(e.NombreCompleto, StringComparison.Ordinal))
        {
            // Los fichajes se identifican por nombre: renombrar a alguien con fichajes los desvincularía
            if (otros.Any(x => x.NombreCompleto.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Ya existe un empleado con ese nombre.");
            if ((await _fichajes.ListarAsync()).Any(f => f.NombreCompleto.Equals(e.NombreCompleto, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("No se puede cambiar el nombre de un empleado que ya tiene fichajes.");
        }

        e.NombreCompleto = nombre;
        e.Email = email;
        await _empleados.ActualizarAsync(e);
        await _historial.RegistrarAsync(e, "EDICION_DATOS", actor);
        await _bitacora.RegistrarAsync(actor, "USUARIO_EDITADO:" + nombre);
    }

    public async Task DarDeBajaAsync(string actor, int id)
    {
        var e = await ObtenerAsync(id);
        await _guarda.ValidarAsync(usuarioId: id, nuevosRolIds: new List<int>());

        // La baja se registra antes de aplicarla para que la foto conserve el último estado activo
        await _historial.RegistrarAsync(e, "BAJA", actor);
        e.Activo = false;
        await _empleados.ActualizarAsync(e);
        await _bitacora.RegistrarAsync(actor, "USUARIO_BAJA:" + e.NombreCompleto);
    }

    public async Task ReactivarAsync(string actor, int id)
    {
        var e = await ObtenerAsync(id);
        e.Activo = true;
        await _empleados.ActualizarAsync(e);
        await _historial.RegistrarAsync(e, "ALTA", actor);
        await _bitacora.RegistrarAsync(actor, "USUARIO_REACTIVADO:" + e.NombreCompleto);
    }

    public async Task DesbloquearAsync(string actor, int id)
    {
        var e = await ObtenerAsync(id);
        e.Bloqueado = false;
        e.IntentosFallidos = 0;
        await _empleados.ActualizarAsync(e);
        await _bitacora.RegistrarAsync(actor, "DESBLOQUEO_USUARIO:" + e.NombreCompleto);
        await _historial.RegistrarAsync(e, "DESBLOQUEO", actor);
    }

    // Un administrador define un PIN nuevo (por ejemplo si el empleado lo olvidó); también desbloquea
    public async Task ResetearPinAsync(string actor, int id, string nuevoPin)
    {
        var errorPin = ValidadorPin.ObtenerError(nuevoPin);
        if (errorPin != null) throw new InvalidOperationException(errorPin);

        var e = await ObtenerAsync(id);
        e.PinHash = BCrypt.Net.BCrypt.HashPassword(nuevoPin);
        e.Bloqueado = false;
        e.IntentosFallidos = 0;
        await _empleados.ActualizarAsync(e);
        await _historial.RegistrarAsync(e, "CAMBIO_PIN", actor);
        await _bitacora.RegistrarAsync(actor, "PIN_RESTABLECIDO:" + e.NombreCompleto);
    }

    // Cada empleado cambia su propio PIN
    public async Task CambiarPinPropioAsync(int id, string pinActual, string pinNuevo)
    {
        var errorPin = ValidadorPin.ObtenerError(pinNuevo);
        if (errorPin != null) throw new InvalidOperationException(errorPin);

        var e = await ObtenerAsync(id);
        if (!BCrypt.Net.BCrypt.Verify(pinActual ?? "", e.PinHash))
            throw new InvalidOperationException("El PIN actual no es correcto.");

        e.PinHash = BCrypt.Net.BCrypt.HashPassword(pinNuevo);
        await _empleados.ActualizarAsync(e);
        await _bitacora.RegistrarAsync(e.Email, "CAMBIO_PIN");
        await _historial.RegistrarAsync(e, "CAMBIO_PIN", e.Email);
    }

    public async Task AsignarRolesAsync(string actor, int id, List<int> rolIds)
    {
        var e = await ObtenerAsync(id);
        await ValidarRolesAsync(rolIds);
        await _guarda.ValidarAsync(usuarioId: id, nuevosRolIds: rolIds);

        await _roles.ReemplazarAsignacionesAsync(id, rolIds);
        await _historial.RegistrarAsync(e, "ASIGNACION_ROL", actor);
        await _bitacora.RegistrarAsync(actor, "ROLES_ASIGNADOS:" + e.NombreCompleto);
    }

    public async Task<List<HistorialUsuario>> HistorialAsync(int id)
    {
        await ObtenerAsync(id);
        return await _historial.ListarAsync(id);
    }

    // Vuelve a una foto anterior: email, activo, bloqueo, intentos y roles. El PIN no se toca.
    // El rollback queda registrado como un cambio más y no puede ser blanco de otro rollback.
    public async Task RollbackAsync(string actor, int historialId)
    {
        var foto = (await _historialRepositorio.ObtenerAsync(historialId)) ?? throw new InvalidOperationException("La versión indicada no existe.");
        if (foto.Tipo == "ROLLBACK")
            throw new InvalidOperationException("No se puede restaurar una versión generada por un rollback.");

        var e = await ObtenerAsync(foto.EmpleadoId);

        var rolesDestino = foto.Activo && !foto.Bloqueado ? foto.RolIds : new List<int>();
        await _guarda.ValidarAsync(usuarioId: e.Id, nuevosRolIds: rolesDestino);

        e.Email = foto.Email;
        e.Activo = foto.Activo;
        e.Bloqueado = foto.Bloqueado;
        e.IntentosFallidos = foto.IntentosFallidos;
        await _empleados.ActualizarAsync(e);
        if (foto.RolIds.Count > 0)
            await _roles.ReemplazarAsignacionesAsync(e.Id, foto.RolIds);

        await _historial.RegistrarAsync(e, "ROLLBACK", actor, versionOrigen: foto.Id);
        await _bitacora.RegistrarAsync(actor, "ROLLBACK_USUARIO:" + e.NombreCompleto);
    }

    // ---- auxiliares ----

    private async Task<EmpleadoDto> ObtenerAsync(int id) =>
        await _empleados.ObtenerPorIdAsync(id) ?? throw new InvalidOperationException("El usuario no existe.");

    private async Task ValidarRolesAsync(List<int> rolIds)
    {
        if (rolIds == null || rolIds.Count == 0)
            throw new InvalidOperationException("Asigná al menos un rol.");

        var existentes = (await _roles.ListarRolesAsync()).Select(r => r.Id).ToHashSet();
        if (rolIds.Any(r => !existentes.Contains(r)))
            throw new InvalidOperationException("Alguno de los roles indicados no existe.");
    }

    private static void ValidarEmail(string email)
    {
        if (email.Length == 0 || !email.Contains('@') || email.Contains(' ') || email.Length > 100)
            throw new InvalidOperationException("Indicá un email válido.");
    }
}
