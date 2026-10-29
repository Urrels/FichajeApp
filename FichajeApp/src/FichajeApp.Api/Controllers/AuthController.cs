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
    private readonly LoginService _loginService;
    private readonly JwtService _jwtService;

    public AuthController(LoginService loginService, JwtService jwtService)
    {
        _loginService = loginService;
        _jwtService = jwtService;
    }

    // Limitado por IP (ver "login" en Program.cs): un PIN de 4 dígitos se prueba entero en minutos sin límite
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto login)
    {
        var resultado = await _loginService.AutenticarAsync(login.Email, login.Pin);

        switch (resultado.Resultado)
        {
            case LoginResultado.UsuarioBloqueado:
                return Unauthorized(new { mensaje = "Usuario bloqueado por intentos fallidos. Pedile a un administrador que lo desbloquee." });

            case LoginResultado.Exito:
                var empleado = resultado.Empleado!;
                return Ok(new LoginResponseDto
                {
                    Token = _jwtService.GenerarToken(empleado, resultado.Roles, resultado.Permisos),
                    NombreCompleto = empleado.NombreCompleto,
                    EmpleadoId = empleado.Id,
                    Rol = resultado.Roles.FirstOrDefault() ?? "Empleado",
                    Roles = resultado.Roles,
                    Permisos = resultado.Permisos
                });

            default:
                return Unauthorized(new { mensaje = "Email o PIN incorrecto" });
        }
    }
}
