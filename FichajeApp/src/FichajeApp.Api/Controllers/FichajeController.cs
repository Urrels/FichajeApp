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
    private readonly IFichajeService _fichajeService;
    private readonly IEmpleadosService _empleadosService;

    private readonly ISitiosService _sitiosService;

    public FichajeController(IFichajeService fichajeService, IEmpleadosService empleadosService,
        ISitiosService sitiosService)
    {
        _fichajeService = fichajeService;
        _empleadosService = empleadosService;
        _sitiosService = sitiosService;
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
