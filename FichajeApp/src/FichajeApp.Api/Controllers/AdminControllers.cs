using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Controllers;

// Las reglas de negocio de los servicios de administración lanzan InvalidOperationException con un mensaje
// pensado para el usuario: se devuelven como 400 { mensaje }. Cualquier otro error sigue siendo un 500.
public class ManejarReglasNegocioAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is InvalidOperationException ex)
        {
            context.Result = new BadRequestObjectResult(new { mensaje = ex.Message });
            context.ExceptionHandled = true;
        }
    }
}

[ApiController]
[ManejarReglasNegocio]
public abstract class AdminControllerBase : ControllerBase
{
    // Quién hace el cambio (queda en la bitácora y en el historial): el email del token
    protected string Actor => User.FindFirst(ClaimTypes.Email)?.Value ?? "desconocido";
}

public record EmpleadoResumenDto(int Id, string Nombre);

// Empleados activos (id y nombre), para los selectores de la app
public static class EmpleadosActivos
{
    public static async Task<List<EmpleadoResumenDto>> ListarAsync(IEmpleadosRepositorio empleados) =>
        (await empleados.ListarAsync()).Where(e => e.Activo)
            .OrderBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EmpleadoResumenDto(e.Id, e.NombreCompleto)).ToList();
}

// ---- Cuenta propia ----

[Route("api/cuenta")]
[RequierePermiso(CatalogoPermisos.CambiarPin)]
public class CuentaController : AdminControllerBase
{
    private readonly UsuariosAdminService _usuarios;
    public CuentaController(UsuariosAdminService usuarios) => _usuarios = usuarios;

    public record CambiarPinRequest(string PinActual, string PinNuevo);

    [HttpPost("cambiar-pin")]
    public async Task<IActionResult> CambiarPin([FromBody] CambiarPinRequest r)
    {
        var id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await _usuarios.CambiarPinPropioAsync(id, r.PinActual, r.PinNuevo);
        return Ok(new { mensaje = "PIN actualizado" });
    }
}

// ---- Usuarios ----

[Route("api/admin/usuarios")]
[RequierePermiso(CatalogoPermisos.AdministrarUsuarios)]
public class UsuariosController : AdminControllerBase
{
    private readonly UsuariosAdminService _usuarios;
    private readonly RolesService _roles;

    public UsuariosController(UsuariosAdminService usuarios, RolesService roles)
    {
        _usuarios = usuarios;
        _roles = roles;
    }

    public record CrearUsuarioRequest(string NombreCompleto, string Email, string Pin, List<int> RolIds);
    public record DatosUsuarioRequest(string NombreCompleto, string Email);
    public record PinRequest(string Pin);
    public record RolesRequest(List<int> RolIds);

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? busqueda, [FromQuery] int pagina = 1, [FromQuery] int tamanio = 25) =>
        Ok(await _usuarios.ListarAsync(busqueda, pagina, tamanio));

    // Roles disponibles para el selector de asignación
    [HttpGet("roles")]
    public async Task<IActionResult> Roles() => Ok(await _roles.ListarRolesAsync());

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioRequest r) =>
        Ok(new { id = await _usuarios.CrearAsync(Actor, r.NombreCompleto, r.Email, r.Pin, r.RolIds), mensaje = "Usuario creado" });

    [HttpPut("{id:int}/datos")]
    public async Task<IActionResult> Datos(int id, [FromBody] DatosUsuarioRequest r)
    {
        await _usuarios.ActualizarDatosAsync(Actor, id, r.NombreCompleto, r.Email);
        return Ok(new { mensaje = "Datos actualizados" });
    }

    [HttpPost("{id:int}/baja")]
    public async Task<IActionResult> Baja(int id)
    {
        await _usuarios.DarDeBajaAsync(Actor, id);
        return Ok(new { mensaje = "Usuario dado de baja" });
    }

    [HttpPost("{id:int}/reactivar")]
    public async Task<IActionResult> Reactivar(int id)
    {
        await _usuarios.ReactivarAsync(Actor, id);
        return Ok(new { mensaje = "Usuario reactivado" });
    }

    [HttpPost("{id:int}/desbloquear")]
    public async Task<IActionResult> Desbloquear(int id)
    {
        await _usuarios.DesbloquearAsync(Actor, id);
        return Ok(new { mensaje = "Usuario desbloqueado" });
    }

    [HttpPost("{id:int}/restablecer-pin")]
    public async Task<IActionResult> RestablecerPin(int id, [FromBody] PinRequest r)
    {
        await _usuarios.ResetearPinAsync(Actor, id, r.Pin);
        return Ok(new { mensaje = "PIN restablecido" });
    }

    [HttpPut("{id:int}/roles")]
    public async Task<IActionResult> AsignarRoles(int id, [FromBody] RolesRequest r)
    {
        await _usuarios.AsignarRolesAsync(Actor, id, r.RolIds);
        return Ok(new { mensaje = "Roles asignados" });
    }

    [HttpGet("{id:int}/historial")]
    public async Task<IActionResult> Historial(int id) => Ok(await _usuarios.HistorialAsync(id));

    [HttpPost("historial/{historialId:int}/rollback")]
    public async Task<IActionResult> Rollback(int historialId)
    {
        await _usuarios.RollbackAsync(Actor, historialId);
        return Ok(new { mensaje = "Usuario restaurado" });
    }
}

// ---- Roles y permisos ----

[Route("api/admin/roles")]
[RequierePermiso(CatalogoPermisos.GestionRoles)]
public class RolesController : AdminControllerBase
{
    private readonly RolesService _roles;
    public RolesController(RolesService roles) => _roles = roles;

    public record NuevoRolRequest(string Nombre, int? PadreId);
    public record NombreRequest(string Nombre);
    public record PadreRequest(int? PadreId);
    public record PermisosRequest(List<int> PermisoIds);

    [HttpGet("arbol")]
    public async Task<IActionResult> Arbol() => Ok(await _roles.ObtenerArbolDtoAsync());

    [HttpGet("permisos")]
    public async Task<IActionResult> Permisos() =>
        Ok((await _roles.ListarPermisosDisponiblesAsync()).Select(p => new { p.Id, p.Nombre }));

    [HttpGet("{id:int}/permisos")]
    public async Task<IActionResult> PermisosDeRol(int id) => Ok(await _roles.ObtenerPermisoIdsDeRolAsync(id));

    [HttpPost]
    public async Task<IActionResult> Agregar([FromBody] NuevoRolRequest r) =>
        Ok(new { id = await _roles.AgregarAsync(Actor, r.Nombre, r.PadreId), mensaje = "Rol creado" });

    [HttpPost("{id:int}/duplicar")]
    public async Task<IActionResult> Duplicar(int id, [FromBody] NombreRequest r) =>
        Ok(new { id = await _roles.DuplicarAsync(Actor, id, r.Nombre), mensaje = "Rol duplicado" });

    [HttpPut("{id:int}/padre")]
    public async Task<IActionResult> CambiarPadre(int id, [FromBody] PadreRequest r)
    {
        await _roles.CambiarPadreAsync(Actor, id, r.PadreId);
        return Ok(new { mensaje = "Rol movido" });
    }

    [HttpPut("{id:int}/permisos")]
    public async Task<IActionResult> ActualizarPermisos(int id, [FromBody] PermisosRequest r)
    {
        await _roles.ActualizarPermisosAsync(Actor, id, r.PermisoIds);
        return Ok(new { mensaje = "Permisos actualizados" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        await _roles.EliminarAsync(Actor, id);
        return Ok(new { mensaje = "Rol eliminado" });
    }
}

// ---- Bitácora ----

[Route("api/admin/bitacora")]
[RequierePermiso(CatalogoPermisos.VerBitacora)]
public class BitacoraController : AdminControllerBase
{
    private readonly BitacoraService _bitacora;
    public BitacoraController(BitacoraService bitacora) => _bitacora = bitacora;

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? usuario, [FromQuery] string? accion,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] int pagina = 1, [FromQuery] int tamanio = 50) =>
        Ok(await _bitacora.ListarAsync(usuario, accion, desde, hasta, pagina, tamanio));
}

// ---- Corrección de fichajes ----

[Route("api/admin/fichajes")]
[RequierePermiso(CatalogoPermisos.CorregirFichajes)]
public class FichajesAdminController : AdminControllerBase
{
    private readonly FichajesAdminService _fichajes;
    private readonly IEmpleadosRepositorio _empleados;

    public FichajesAdminController(FichajesAdminService fichajes, IEmpleadosRepositorio empleados)
    {
        _fichajes = fichajes;
        _empleados = empleados;
    }

    public record CorregirRequest(int EntradaId, DateTime Ingreso, DateTime Egreso, string Motivo);
    public record ManualRequest(string Empleado, DateTime Ingreso, DateTime Egreso, string Motivo);
    public record CompletarEntradaRequest(int SalidaId, DateTime Ingreso, string Motivo);

    // Para el selector de empleado de la carga manual (este permiso no incluye listar usuarios)
    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados() => Ok(await EmpleadosActivos.ListarAsync(_empleados));

    [HttpPost("completar-entrada")]
    public async Task<IActionResult> CompletarEntrada([FromBody] CompletarEntradaRequest r)
    {
        await _fichajes.CompletarEntradaAsync(Actor, r.SalidaId, r.Ingreso, r.Motivo);
        return Ok(new { mensaje = "Entrada cargada" });
    }

    [HttpGet("incompletos")]
    public async Task<IActionResult> Incompletos([FromQuery] DateTime desde, [FromQuery] DateTime hasta, [FromQuery] bool soloVencidos = false) =>
        Ok(await _fichajes.ListarIncompletosAsync(desde, hasta, soloVencidos));

    [HttpPost("corregir")]
    public async Task<IActionResult> Corregir([FromBody] CorregirRequest r)
    {
        await _fichajes.CorregirAsync(Actor, r.EntradaId, r.Ingreso, r.Egreso, r.Motivo);
        return Ok(new { mensaje = "Fichaje corregido" });
    }

    [HttpPost("manual")]
    public async Task<IActionResult> Manual([FromBody] ManualRequest r)
    {
        await _fichajes.AgregarManualAsync(Actor, r.Empleado, r.Ingreso, r.Egreso, r.Motivo);
        return Ok(new { mensaje = "Fichaje cargado" });
    }
}

// ---- Liquidación de horas ----

[Route("api/admin/liquidacion")]
[RequierePermiso(CatalogoPermisos.LiquidarHoras)]
public class LiquidacionController : AdminControllerBase
{
    private readonly LiquidacionService _liquidacion;
    private readonly IEmpleadosRepositorio _empleados;

    public LiquidacionController(LiquidacionService liquidacion, IEmpleadosRepositorio empleados)
    {
        _liquidacion = liquidacion;
        _empleados = empleados;
    }

    // Para el selector de empleado del valor hora (este permiso no incluye listar usuarios)
    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados() => Ok(await EmpleadosActivos.ListarAsync(_empleados));

    public record PeriodoRequest(DateTime Desde, DateTime Hasta);
    public record ExcedenteRequest(string Empleado, DateTime Fecha);
    public record ValorHoraRequest(int EmpleadoId, decimal Valor, DateTime VigenteDesde);
    public record FeriadoRequest(DateTime Fecha, string Descripcion);

    // Cálculo en vivo del período (no guarda nada)
    [HttpGet("calcular")]
    public async Task<IActionResult> Calcular([FromQuery] DateTime desde, [FromQuery] DateTime hasta, [FromQuery] string? empleado)
    {
        var dias = await _liquidacion.CalcularAsync(desde, hasta, empleado);
        return Ok(new { dias, resumen = LiquidacionService.Resumir(dias) });
    }

    [HttpPost("cerrar")]
    public async Task<IActionResult> Cerrar([FromBody] PeriodoRequest r) =>
        Ok(await _liquidacion.CerrarAsync(Actor, r.Desde, r.Hasta));

    [HttpGet("liquidaciones")]
    public async Task<IActionResult> Liquidaciones() => Ok(await _liquidacion.ListarLiquidacionesAsync());

    [HttpGet("liquidaciones/{id:int}")]
    public async Task<IActionResult> Detalle(int id)
    {
        var dias = await _liquidacion.ObtenerDiasDeLiquidacionAsync(id);
        return Ok(new { dias, resumen = LiquidacionService.Resumir(dias) });
    }

    [HttpPost("liquidaciones/{id:int}/pagar")]
    public async Task<IActionResult> Pagar(int id)
    {
        await _liquidacion.MarcarPagadaAsync(Actor, id);
        return Ok(new { mensaje = "Liquidación marcada como pagada" });
    }

    // Resumen por empleado de una liquidación cerrada, para llevar a sueldos (Excel en configuración regional argentina)
    [HttpGet("liquidaciones/{id:int}/csv")]
    public async Task<IActionResult> Csv(int id)
    {
        var resumen = LiquidacionService.Resumir(await _liquidacion.ObtenerDiasDeLiquidacionAsync(id));
        var ar = new System.Globalization.CultureInfo("es-AR");

        var sb = new StringBuilder();
        sb.AppendLine("Empleado;Horas trabajadas;Horas a pagar;Horas normales;Horas extra;Horas domingo/feriado;Ausencias;Importe");
        foreach (var r in resumen)
            sb.AppendLine(string.Join(';', Celda(r.Empleado), LiquidacionCalculo.Horas(r.MinTrabajados).ToString(ar),
                LiquidacionCalculo.Horas(r.MinPagables).ToString(ar), LiquidacionCalculo.Horas(r.MinNormales).ToString(ar),
                LiquidacionCalculo.Horas(r.MinExtra).ToString(ar), LiquidacionCalculo.Horas(r.MinFeriado).ToString(ar),
                r.Ausencias, r.Importe.ToString("0.00", ar)));

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"liquidacion-{id}.csv");
    }

    // Un nombre que empiece con = + - @ podría ejecutarse como fórmula al abrir el CSV en Excel
    private static string Celda(string texto) =>
        texto.Length > 0 && "=+-@".Contains(texto[0]) ? "'" + texto : texto;

    [HttpPost("excedentes/aprobar")]
    public async Task<IActionResult> AprobarExcedente([FromBody] ExcedenteRequest r)
    {
        await _liquidacion.AprobarExcedenteAsync(Actor, r.Empleado, r.Fecha);
        return Ok(new { mensaje = "Excedente aprobado" });
    }

    [HttpPost("excedentes/revocar")]
    public async Task<IActionResult> RevocarExcedente([FromBody] ExcedenteRequest r)
    {
        await _liquidacion.RevocarExcedenteAsync(Actor, r.Empleado, r.Fecha);
        return Ok(new { mensaje = "Excedente revocado" });
    }

    [HttpGet("parametros")]
    public async Task<IActionResult> Parametros() => Ok(await _liquidacion.ObtenerParametrosAsync());

    [HttpPut("parametros")]
    public async Task<IActionResult> GuardarParametros([FromBody] ParametrosLiquidacion p)
    {
        await _liquidacion.GuardarParametrosAsync(Actor, p);
        return Ok(new { mensaje = "Parámetros guardados" });
    }

    [HttpGet("valor-hora")]
    public async Task<IActionResult> ValoresHora() => Ok(await _liquidacion.ListarValoresHoraAsync());

    [HttpPut("valor-hora")]
    public async Task<IActionResult> GuardarValorHora([FromBody] ValorHoraRequest r)
    {
        await _liquidacion.GuardarValorHoraAsync(Actor, r.EmpleadoId, r.Valor, r.VigenteDesde);
        return Ok(new { mensaje = "Valor hora guardado" });
    }

    [HttpGet("feriados")]
    public async Task<IActionResult> Feriados([FromQuery] DateTime desde, [FromQuery] DateTime hasta) =>
        Ok(await _liquidacion.ListarFeriadosAsync(desde, hasta));

    [HttpPut("feriados")]
    public async Task<IActionResult> GuardarFeriado([FromBody] FeriadoRequest r)
    {
        await _liquidacion.GuardarFeriadoAsync(Actor, r.Fecha, r.Descripcion);
        return Ok(new { mensaje = "Feriado guardado" });
    }

    [HttpDelete("feriados")]
    public async Task<IActionResult> EliminarFeriado([FromQuery] DateTime fecha)
    {
        await _liquidacion.EliminarFeriadoAsync(Actor, fecha);
        return Ok(new { mensaje = "Feriado eliminado" });
    }
}
