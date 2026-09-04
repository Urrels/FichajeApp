using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Models;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly GoogleSheetsEmpleadosService _empleadosService;
    private readonly JwtService _jwtService;

    public AuthController(GoogleSheetsEmpleadosService empleadosService, JwtService jwtService)
    {
        _empleadosService = empleadosService;
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto login)
    {
        var empleado = await _empleadosService.BuscarPorEmailAsync(login.Email);

        if (empleado is null || !BCrypt.Net.BCrypt.Verify(login.Pin, empleado.PinHash))
            return Unauthorized(new { mensaje = "Email o PIN incorrecto" });

        var token = _jwtService.GenerarToken(empleado);

        return Ok(new LoginResponseDto
        {
            Token = token,
            NombreCompleto = empleado.NombreCompleto,
            EmpleadoId = empleado.Id
        });
    }
}
