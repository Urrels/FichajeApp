using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Servicios;

public record KioscoEmpleadoDto(int Id, string Nombre, string Proximo);

public record ResultadoKiosco(bool Exito, string Mensaje, string? Codigo = null, FichajeDto? Fichaje = null);

// Teléfono compartido de una sucursal: una cuenta "Kiosco" queda con la sesión abierta y cada empleado ficha
// eligiendo su nombre y poniendo su PIN. El fichaje queda a nombre del empleado y con el nombre de la sucursal como lugar.
public class KioscoService
{
    private readonly IEmpleadosRepositorio _empleados;
    private readonly PermisosService _permisos;
    private readonly IFichajeStorageStrategy _storage;
    private readonly LoginService _login;
    private readonly IFichajeService _fichaje;

    public KioscoService(IEmpleadosRepositorio empleados, PermisosService permisos, IFichajeStorageStrategy storage,
        LoginService login, IFichajeService fichaje)
    {
        _empleados = empleados;
        _permisos = permisos;
        _storage = storage;
        _login = login;
        _fichaje = fichaje;
    }

    // Quiénes pueden fichar acá (activos, no bloqueados y con el permiso de fichar: la cuenta de la sucursal no figura)
    // y qué les toca ahora, para que la pantalla diga "Entrada" o "Salida" antes de elegirse.
    public async Task<List<KioscoEmpleadoDto>> ListarAsync()
    {
        var rolesDeUsuarios = await _permisos.RolesDeUsuariosActivosAsync();
        var permisosPorRol = await _permisos.PermisosPorRolAsync();

        var hoy = DateTime.Now.Date;
        var ultimoDeHoy = (await _storage.ObtenerFichajesAsync())
            .Where(f => f.FechaHora.Date == hoy)
            .GroupBy(f => f.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.FechaHora).Last().TipoFichaje, StringComparer.OrdinalIgnoreCase);

        return (await _empleados.ListarAsync())
            .Where(e => rolesDeUsuarios.TryGetValue(e.Id, out var roles)
                        && roles.Any(r => permisosPorRol.TryGetValue(r, out var p) && p.Contains(CatalogoPermisos.FicharAsistencia)))
            .OrderBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(e => new KioscoEmpleadoDto(e.Id, e.NombreCompleto,
                ultimoDeHoy.GetValueOrDefault(e.NombreCompleto) == "Entrada" ? "Salida" : "Entrada"))
            .ToList();
    }

    public async Task<ResultadoKiosco> FicharAsync(string sucursal, int empleadoId, string? pin, byte[]? foto)
    {
        var auth = await _login.AutenticarPorIdAsync(empleadoId, pin);
        switch (auth.Resultado)
        {
            case LoginResultado.UsuarioBloqueado:
                return new ResultadoKiosco(false, "Usuario bloqueado por intentos fallidos. Pedile a un administrador que lo desbloquee.", "BLOQUEADO");
            case LoginResultado.Exito:
                break;
            default:
                return new ResultadoKiosco(false, "PIN incorrecto.", "PIN_INCORRECTO");
        }

        var empleado = auth.Empleado!;
        if (!auth.Permisos.Contains(CatalogoPermisos.FicharAsistencia))
            return new ResultadoKiosco(false, "Tu usuario no tiene permiso para fichar.", "SIN_PERMISO");

        var contexto = new FichajeContext { Empleado = empleado, LugarFijo = sucursal, Foto = foto };
        var validacion = await _fichaje.ValidarAsync(contexto);
        if (!validacion.EsValido)
            return new ResultadoKiosco(false, validacion.Mensaje, validacion.Codigo);

        var fichaje = await _fichaje.RegistrarAsync(empleado, contexto);
        return new ResultadoKiosco(true, $"{fichaje.TipoFichaje} registrada: {empleado.NombreCompleto}", Fichaje: fichaje);
    }
}
