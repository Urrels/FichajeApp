using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequierePermiso(CatalogoPermisos.FicharAsistencia)]
public class FichajeController : ControllerBase
{
    private readonly IFichajeService _fichajeService;
    private readonly IEmpleadosRepositorio _empleadosService;
    private readonly ISitiosService _sitiosService;
    private readonly ILogger<FichajeController> _log;

    public FichajeController(IFichajeService fichajeService, IEmpleadosRepositorio empleadosService,
        ISitiosService sitiosService, ILogger<FichajeController> log)
    {
        _log = log;
        _fichajeService = fichajeService;
        _empleadosService = empleadosService;
        _sitiosService = sitiosService;
    }

    // Cómo se ficha en este cliente (si pide ubicación y foto): la app lo consulta antes de fichar
    [HttpGet("configuracion")]
    public async Task<IActionResult> Configuracion()
    {
        var c = await _fichajeService.ConfiguracionAsync();
        return Ok(new { ubicacion = c.Ubicacion, foto = c.Foto, retencionDias = c.RetencionFotosDias, precisionMaxMetros = c.PrecisionMaxMetros });
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

        var config = await _fichajeService.ConfiguracionAsync();

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

        if (!string.IsNullOrWhiteSpace(request.FotoBase64) && config.Foto != ConfiguracionFichaje.FotoNo)
        {
            try { contexto.Foto = ReglasFotos.Decodificar(request.FotoBase64); }
            catch (FormatException ex) { return BadRequest(new { mensaje = ex.Message, codigo = "FOTO_INVALIDA" }); }
        }

        if (config.Ubicacion == ConfiguracionFichaje.UbicacionLibre)
            contexto.Sitio = await _sitiosService.BuscarSitioAsync(request.Latitud, request.Longitud);

        var validacion = await _fichajeService.ValidarAsync(contexto);
        if (!validacion.EsValido)
        {
            // Queda en el registro de la API: sirve para entender por qué a alguien "no le deja fichar"
            _log.LogWarning("Fichaje rechazado de {Email}: {Mensaje} (código {Codigo}, precisión {Precision} m, máximo {Maximo} m)",
                email, validacion.Mensaje, validacion.Codigo ?? "-", request.Precision?.ToString("0") ?? "?", config.PrecisionMaxMetros);
            return BadRequest(new { mensaje = validacion.Mensaje, codigo = validacion.Codigo });
        }

        var fichaje = await _fichajeService.RegistrarAsync(empleado!, contexto);

        return Ok(new { mensaje = $"{fichaje.TipoFichaje} registrada correctamente", tipo = fichaje.TipoFichaje });
    }
}
