using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
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
    public async Task<IActionResult> Estado()
    {
        // "configurada" es true solo si hay una planilla conectada Y todavía existe y está compartida: si se borró,
        // la app lo toma como "sin configurar" y ofrece volver a conectar (con el aviso de por qué)
        var e = await _conexion.EstadoAsync();
        return Ok(new { configurada = e.Lista, cuentaDeServicio = _conexion.CuentaDeServicio, aviso = e.Aviso });
    }

    [HttpPost("google")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Configurar([FromBody] ConfigurarConexionRequest request)
    {
        // La autenticación corre aunque la acción sea anónima: si viene el token de un administrador, se lo reconoce
        var acceso = await _conexion.VerificarAccesoAsync(User.HasClaim(ClaimsPermisos.Permiso, CatalogoPermisos.CambiarConexion), request.Clave);
        if (acceso == ConexionService.Acceso.ClaveIncorrecta)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "El código de activación no es correcto." });
        if (acceso == ConexionService.Acceso.SoloAdministrador)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "La planilla ya está conectada. Para cambiarla, iniciá sesión como administrador." });

        try
        {
            var r = await _conexion.ConfigurarAsync(request);
            var mensaje = r.PlanillaCreadaUrl is null
                ? "Conexión guardada. Ya podés iniciar sesión."
                : $"Listo. Creamos la planilla «{r.PlanillaCreadaTitulo}» y te la compartimos en {r.CompartidaCon}. " +
                  "Buscala en tu Google Drive, en «Compartidos conmigo» (o en tu mail). Ya podés iniciar sesión.";
            return Ok(new { mensaje, cuentaDeServicio = r.CuentaDeServicio, planillaUrl = r.PlanillaCreadaUrl });
        }
        catch (ConexionInvalidaException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
