using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

// Solo para quien tiene el permiso "Ver panel de supervisor" (claim "permiso" del JWT)
[ApiController]
[Route("api/[controller]")]
[RequierePermiso(CatalogoPermisos.VerPanelSupervisor)]
public class SupervisorController : ControllerBase
{
    private const int MaxDias = 62;

    private readonly ConsultaFichajesService _consulta;
    private readonly IEmpleadosRepositorio _empleados;

    public SupervisorController(ConsultaFichajesService consulta, IEmpleadosRepositorio empleados)
    {
        _consulta = consulta;
        _empleados = empleados;
    }

    // GET api/supervisor/fichajes?desde=2026-10-01&hasta=2026-10-31&empleado=Nombre
    [HttpGet("fichajes")]
    public async Task<IActionResult> Fichajes([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] string? empleado)
    {
        var hoy = DateTime.Now.Date;
        var d = (desde ?? hoy).Date;
        var h = (hasta ?? d).Date;

        if (h < d)
            return BadRequest(new { mensaje = "La fecha 'hasta' no puede ser anterior a 'desde'" });
        if ((h - d).TotalDays > MaxDias)
            return BadRequest(new { mensaje = $"El rango no puede superar {MaxDias} días" });

        return Ok(await _consulta.ObtenerAsync(d, h, empleado));
    }

    // GET api/supervisor/presentes
    [HttpGet("presentes")]
    public async Task<IActionResult> Presentes() => Ok(await _consulta.ObtenerPresentesAsync());

    // GET api/supervisor/empleados  -> nombres de los empleados activos (para el filtro)
    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados()
    {
        var empleados = await _empleados.ListarAsync();
        return Ok(empleados.Where(e => e.Activo).Select(e => e.NombreCompleto).OrderBy(n => n).ToList());
    }
}
