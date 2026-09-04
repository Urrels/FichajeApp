using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Models;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FichajeController : ControllerBase
{
    private readonly FichajeService _fichajeService;
    private readonly GoogleSheetsEmpleadosService _empleadosService;

    public FichajeController(FichajeService fichajeService, GoogleSheetsEmpleadosService empleadosService)
    {
        _fichajeService = fichajeService;
        _empleadosService = empleadosService;
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
            Longitud = request.Longitud
        };

        var validacion = _fichajeService.Validar(contexto);
        if (!validacion.EsValido)
            return BadRequest(new { mensaje = validacion.Mensaje });

        var fichaje = await _fichajeService.RegistrarAsync(empleado!, request.Latitud, request.Longitud);

        return Ok(new { mensaje = $"{fichaje.TipoFichaje} registrada correctamente", tipo = fichaje.TipoFichaje });
    }
}
