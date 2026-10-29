using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequierePermiso(CatalogoPermisos.FicharAsistencia)]
public class FichajeController : ControllerBase
{
    private readonly FichajeService _fichajeService;
    private readonly GoogleSheetsEmpleadosService _empleadosService;

    private readonly GoogleSheetsSitiosService _sitiosService;

    public FichajeController(FichajeService fichajeService, GoogleSheetsEmpleadosService empleadosService,
        GoogleSheetsSitiosService sitiosService)
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
