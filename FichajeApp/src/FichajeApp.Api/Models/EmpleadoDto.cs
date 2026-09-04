namespace FichajeApp.Api.Models;

public class EmpleadoDto
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PinHash { get; set; } = default!;
    public bool Activo { get; set; }
}
