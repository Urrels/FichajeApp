namespace FichajeApp.Api.Models;

public class LoginRequestDto
{
    public string Email { get; set; } = default!;
    public string Pin { get; set; } = default!;
}

public class LoginResponseDto
{
    public string Token { get; set; } = default!;
    public string NombreCompleto { get; set; } = default!;
    public int EmpleadoId { get; set; }
    public string Rol { get; set; } = "Empleado";
}
