using FichajeApp.Api.Dominio.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string MensajeCredenciales = "Email o PIN incorrecto. Después de 3 intentos fallidos hay que esperar 15 minutos.";

    private readonly LoginService _loginService;
    private readonly JwtService _jwtService;
    private readonly RecuperacionDePin _recuperacion;

    public AuthController(LoginService loginService, JwtService jwtService, RecuperacionDePin recuperacion)
    {
        _loginService = loginService;
        _jwtService = jwtService;
        _recuperacion = recuperacion;
    }

    // Limitado por IP (ver "login" en Program.cs): un PIN de 4 dígitos se prueba entero en minutos sin límite
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto login)
    {
        var resultado = await _loginService.AutenticarAsync(login.Email, login.Pin);

        switch (resultado.Resultado)
        {
            // Mismo mensaje que un PIN incorrecto: si dijera "bloqueado" delataría que ese mail existe
            case LoginResultado.UsuarioBloqueado:
                return Unauthorized(new { mensaje = MensajeCredenciales });

            case LoginResultado.Exito:
                var empleado = resultado.Empleado!;
                return Ok(new LoginResponseDto
                {
                    // El teléfono de una sucursal queda abierto: su sesión dura dos semanas (antes, un mes). Si se pierde el
                    // teléfono, dar de baja la cuenta de la sucursal corta la sesión enseguida.
                    Token = _jwtService.GenerarToken(empleado, resultado.Roles, resultado.Permisos,
                        CatalogoPermisos.EsSoloKiosco(resultado.Permisos) ? TimeSpan.FromDays(14) : null),
                    NombreCompleto = empleado.NombreCompleto,
                    EmpleadoId = empleado.Id,
                    Rol = resultado.Roles.FirstOrDefault() ?? "Empleado",
                    Roles = resultado.Roles,
                    Permisos = resultado.Permisos
                });

            default:
                return Unauthorized(new { mensaje = MensajeCredenciales });
        }
    }

    public record OlvidePinRequest(string? Email);
    public record RestablecerPinRequest(string? Email, string? Codigo, string? PinNuevo);

    // Manda un código por mail. La respuesta es la misma exista o no la cuenta (no delata qué mails están dados de alta).
    [HttpPost("olvide-pin")]
    [EnableRateLimiting("login")]
    [ManejarReglasNegocio]
    public async Task<IActionResult> OlvidePin([FromBody] OlvidePinRequest r)
    {
        if (!_recuperacion.CorreoDisponible)
            return StatusCode(503, new { mensaje = "Esta empresa todavía no activó el envío de mails. Pedile a tu administrador que restablezca tu PIN." });

        await _recuperacion.SolicitarAsync(r.Email);
        return Ok(new { mensaje = "Si el email está dado de alta, te mandamos un código. Revisá también la carpeta de spam." });
    }

    [HttpPost("restablecer-pin")]
    [EnableRateLimiting("login")]
    [ManejarReglasNegocio]
    public async Task<IActionResult> RestablecerPin([FromBody] RestablecerPinRequest r)
    {
        await _recuperacion.RestablecerAsync(r.Email, r.Codigo, r.PinNuevo);
        return Ok(new { mensaje = "Listo, ya podés ingresar con tu PIN nuevo." });
    }
}
