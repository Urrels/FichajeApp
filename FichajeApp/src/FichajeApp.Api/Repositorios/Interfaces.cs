using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Repositorios;

// Acceso a datos detrás de interfaces (Dependency Inversion): hoy Google Sheets, mañana una base de datos.
// Los servicios solo conocen estas interfaces, lo que permite probarlos con repositorios en memoria.

public interface IEmpleadosRepositorio
{
    Task<List<EmpleadoDto>> ListarAsync();
    Task<EmpleadoDto?> ObtenerPorIdAsync(int id);
    Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false);
    Task<EmpleadoDto> CrearAsync(EmpleadoDto empleado);   // asigna el Id
    Task ActualizarAsync(EmpleadoDto empleado);
}

public interface IRolesRepositorio
{
    Task<List<Rol>> ListarRolesAsync();
    Task<List<Permiso>> ListarPermisosAsync();
    Task<List<(int RolId, int PermisoId)>> ListarRolPermisosAsync();
    Task<int> InsertarRolAsync(string nombre, int? padreId, bool protegido = false);
    Task CambiarPadreAsync(int rolId, int? padreId);
    // Elimina los roles y, en cascada, sus permisos y sus asignaciones a usuarios
    Task EliminarRolesAsync(IReadOnlyCollection<int> rolIds);
    Task GuardarPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds);
    Task<Dictionary<int, List<int>>> ListarAsignacionesAsync();   // empleadoId -> rolIds
    Task ReemplazarAsignacionesAsync(int empleadoId, IEnumerable<int> rolIds);
    Task InsertarPermisoAsync(string nombre);
}

public interface IBitacoraRepositorio
{
    Task RegistrarAsync(EntradaBitacora entrada);
    Task<List<EntradaBitacora>> ListarAsync();
}

public interface IHistorialUsuariosRepositorio
{
    Task<int> RegistrarAsync(HistorialUsuario foto);   // devuelve el Id
    Task<List<HistorialUsuario>> ListarAsync(int? empleadoId = null);
    Task<HistorialUsuario?> ObtenerAsync(int id);
}

public interface IFichajesRepositorio
{
    Task<List<FichajeRegistrado>> ListarAsync();
    Task<FichajeRegistrado?> ObtenerAsync(int id);
    // Cambia la fecha/hora de un evento y deja constancia de quién, cuándo y por qué
    Task CorregirFechaHoraAsync(int id, DateTime nuevaFechaHora, string motivo, string corregidoPor, DateTime ahora);
    Task AgregarManualAsync(string empleado, string tipo, DateTime fechaHora, string motivo, string corregidoPor, DateTime ahora);
}

public interface ILiquidacionRepositorio
{
    Task<ParametrosLiquidacion> ObtenerParametrosAsync();
    Task GuardarParametrosAsync(ParametrosLiquidacion parametros);

    Task<List<ValorHoraEmpleado>> ListarValoresHoraAsync();
    Task GuardarValorHoraAsync(ValorHoraEmpleado valor);

    Task<List<Feriado>> ListarFeriadosAsync();
    Task GuardarFeriadoAsync(Feriado feriado);
    Task EliminarFeriadoAsync(DateTime fecha);

    Task<List<(string Empleado, DateTime Fecha)>> ListarExcedentesAprobadosAsync();
    Task AprobarExcedenteAsync(string empleado, DateTime fecha, string aprobadoPor);
    Task RevocarExcedenteAsync(string empleado, DateTime fecha);

    Task<List<PeriodoLiquidado>> ListarLiquidacionesAsync();
    Task<bool> HayLiquidacionEnRangoAsync(DateTime desde, DateTime hasta);
    Task<int> GuardarLiquidacionAsync(PeriodoLiquidado periodo, IReadOnlyCollection<LiquidacionDia> dias);
    Task MarcarPagadaAsync(int liquidacionId, string pagadaPor, DateTime fechaPago);
    Task<List<LiquidacionDia>> ListarDiasDeLiquidacionAsync(int liquidacionId);
}
