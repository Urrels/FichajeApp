using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

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
    private readonly IAlmacenFotos _fotos;
    private readonly Servicios.JerarquiaService _jerarquia;

    public SupervisorController(ConsultaFichajesService consulta, IEmpleadosRepositorio empleados, IAlmacenFotos fotos,
        Servicios.JerarquiaService jerarquia)
    {
        _consulta = consulta;
        _empleados = empleados;
        _fotos = fotos;
        _jerarquia = jerarquia;
    }

    // A quién puede ver quien hace el pedido: el administrador a todos, el supervisor solo a sus empleados asignados
    private Task<Servicios.AlcanceDeUsuario> AlcanceAsync() =>
        _jerarquia.AlcanceDeAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);

    private IActionResult FueraDeAlcance() =>
        StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Esa persona no está a tu cargo." });

    // GET api/supervisor/foto?archivo=2026/10/29/<id>.jpg  -> la foto de un fichaje (solo de las personas a cargo)
    [HttpGet("foto")]
    public async Task<IActionResult> Foto([FromQuery] string archivo)
    {
        var alcance = await AlcanceAsync();
        if (!alcance.EsGlobal)
        {
            // La foto pertenece al fichaje que la guardó: solo se entrega si esa persona está a cargo de quien pide
            var dueno = (await _consulta.ObtenerFotoDeAsync(archivo ?? ""));
            if (dueno is null || !alcance.Incluye(dueno)) return FueraDeAlcance();
        }

        var foto = await _fotos.LeerAsync(archivo ?? "");
        if (foto is null) return NotFound(new { mensaje = "La foto ya no está disponible (las fotos se borran pasado un tiempo)." });

        Response.Headers.CacheControl = "private,max-age=3600";
        return File(foto.Value.Bytes, foto.Value.Mime);
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

        var alcance = await AlcanceAsync();
        if (!string.IsNullOrWhiteSpace(empleado) && !alcance.Incluye(empleado)) return FueraDeAlcance();

        var fichajes = await _consulta.ObtenerAsync(d, h, empleado);
        return Ok(fichajes.Where(f => alcance.Incluye(f.NombreCompleto)).ToList());
    }

    // GET api/supervisor/presentes
    [HttpGet("presentes")]
    public async Task<IActionResult> Presentes()
    {
        var alcance = await AlcanceAsync();
        return Ok((await _consulta.ObtenerPresentesAsync()).Where(f => alcance.Incluye(f.NombreCompleto)).ToList());
    }

    // GET api/supervisor/recorrido?empleado=Nombre&fecha=2026-10-29  -> los puntos de ubicación de esa persona ese día
    [HttpGet("recorrido")]
    public async Task<IActionResult> Recorrido([FromQuery] string empleado, [FromQuery] DateTime? fecha, [FromServices] Servicios.RecorridoService recorrido)
    {
        var persona = (await _empleados.ListarAsync()).FirstOrDefault(e => e.NombreCompleto.Equals(empleado ?? "", StringComparison.OrdinalIgnoreCase));
        if (persona is null) return NotFound(new { mensaje = "No encontramos a esa persona." });
        if (!(await AlcanceAsync()).Incluye(persona.NombreCompleto)) return FueraDeAlcance();

        var puntos = await recorrido.ObtenerAsync(persona.Id, (fecha ?? DateTime.Now).Date);
        return Ok(puntos.Select(p => new { p.FechaHora, p.Latitud, p.Longitud, p.Precision }));
    }

    // GET api/supervisor/empleados  -> nombres de los empleados activos (para el filtro)
    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados()
    {
        var alcance = await AlcanceAsync();
        var empleados = await _empleados.ListarAsync();
        return Ok(empleados.Where(e => e.Activo && alcance.Incluye(e.NombreCompleto)).Select(e => e.NombreCompleto).OrderBy(n => n).ToList());
    }
}
