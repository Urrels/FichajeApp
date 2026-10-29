using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

// Solo para empleados con Rol = "Supervisor" (claim de rol del JWT)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Supervisor")]
public class SupervisorController : ControllerBase
{
    private const int MaxDias = 62;

    private readonly ConsultaFichajesService _consulta;
    private readonly GoogleSheetsEmpleadosService _empleadosService;

    public SupervisorController(ConsultaFichajesService consulta, GoogleSheetsEmpleadosService empleadosService)
    {
        _consulta = consulta;
        _empleadosService = empleadosService;
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
        var empleados = await _empleadosService.ObtenerEmpleadosAsync();
        return Ok(empleados.Where(e => e.Activo).Select(e => e.NombreCompleto).OrderBy(n => n).ToList());
    }
}
