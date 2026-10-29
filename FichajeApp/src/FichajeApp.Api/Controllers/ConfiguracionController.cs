using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Controllers;

// Configuración de la conexión con Google Sheets desde la app. Se protege con la clave de instalación
// (la define quien instala el servidor), porque sirve incluso cuando todavía no existe ningún usuario.
[ApiController]
[Route("api/configuracion")]
[AllowAnonymous]
public class ConfiguracionController : ControllerBase
{
    private readonly ConexionService _conexion;

    public ConfiguracionController(ConexionService conexion) => _conexion = conexion;

    // La app lo consulta al abrir: si falta configurar, lleva a la pantalla de configuración
    [HttpGet("estado")]
    public IActionResult Estado() => Ok(new { configurada = _conexion.Configurada, cuentaDeServicio = _conexion.CuentaDeServicio });

    [HttpPost("google")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Configurar([FromBody] ConfigurarConexionRequest request)
    {
        if (!_conexion.ClaveCorrecta(request.Clave))
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "La clave de instalación no es correcta." });

        try
        {
            var correo = await _conexion.ConfigurarAsync(request);
            return Ok(new { mensaje = "Conexión guardada. Ya podés iniciar sesión.", cuentaDeServicio = correo });
        }
        catch (ConexionInvalidaException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
