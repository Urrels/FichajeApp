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
    private readonly bool _usaSql;

    public ConfiguracionController(ConexionService conexion, IConfiguration configuracion)
    {
        _conexion = conexion;
        // Con base de datos SQL no hay planilla que conectar: la app no ofrece esa pantalla
        _usaSql = !string.IsNullOrWhiteSpace(configuracion.GetConnectionString("Fichaje"));
    }

    // La app lo consulta al abrir: si falta configurar, lleva a la pantalla de configuración
    [HttpGet("estado")]
    public async Task<IActionResult> Estado()
    {
        // "configurada" es true solo si hay una planilla conectada Y todavía existe y está compartida: si se borró,
        // la app lo toma como "sin configurar" y ofrece volver a conectar (con el aviso de por qué)
        if (_usaSql) return Ok(new { configurada = true, cuentaDeServicio = (string?)null, aviso = (string?)null });

        var e = await _conexion.EstadoAsync();
        return Ok(new { configurada = e.Lista, cuentaDeServicio = _conexion.CuentaDeServicio, aviso = e.Aviso });
    }

    [HttpPost("google")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Configurar([FromBody] ConfigurarConexionRequest request)
    {
        if (_usaSql)
            return Conflict(new { mensaje = "Este servidor guarda los datos en una base de datos SQL: no hay planilla de Google para conectar." });

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
