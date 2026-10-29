using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequierePermiso(CatalogoPermisos.FicharAsistencia)]
public class FichajeController : ControllerBase
{
    private readonly IFichajeService _fichajeService;
    private readonly IEmpleadosRepositorio _empleadosService;
    private readonly ISitiosService _sitiosService;

    public FichajeController(IFichajeService fichajeService, IEmpleadosRepositorio empleadosService,
        ISitiosService sitiosService)
    {
        _fichajeService = fichajeService;
        _empleadosService = empleadosService;
        _sitiosService = sitiosService;
    }

    // Qué va a ser el próximo fichaje del usuario (Entrada o Salida), para mostrarlo antes de fichar
    [HttpGet("estado")]
    public async Task<IActionResult> Estado()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
            return Unauthorized();

        var empleado = await _empleadosService.BuscarPorEmailAsync(email);
        if (empleado is null)
            return NotFound(new { mensaje = "Empleado no encontrado" });

        return Ok(new { proximo = await _fichajeService.ProximoTipoAsync(empleado.NombreCompleto) });
    }

    [HttpPost]
    public async Task<IActionResult> RegistrarFichaje([FromBody] FichajeRequestDto request)
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
            return Unauthorized();

        var empleado = await _empleadosService.BuscarPorEmailAsync(email);

        var contexto = new FichajeContext
        {
            Empleado = empleado,
            Latitud = request.Latitud,
            Longitud = request.Longitud,
            Precision = request.Precision,
            EsUbicacionFalsa = request.EsUbicacionFalsa,
            Direccion = request.Direccion,
            Motivo = request.Motivo?.Trim()
        };

        if (_fichajeService.ModoLibre)
            contexto.Sitio = await _sitiosService.BuscarSitioAsync(request.Latitud, request.Longitud);

        var validacion = _fichajeService.Validar(contexto);
        if (!validacion.EsValido)
            return BadRequest(new { mensaje = validacion.Mensaje, codigo = validacion.Codigo });

        var fichaje = await _fichajeService.RegistrarAsync(empleado!, contexto);

        return Ok(new { mensaje = $"{fichaje.TipoFichaje} registrada correctamente", tipo = fichaje.TipoFichaje });
    }
}
