using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Services;

public class JwtService
{
    private readonly string _secret;
    private readonly string _issuer;

    public JwtService(IConfiguration config)
    {
        _secret = config["Jwt:Secret"]!;
        _issuer = config["Jwt:Issuer"]!;
    }

    // El token lleva los roles y los permisos del usuario: los endpoints se protegen por permiso.
    // Un cambio de roles se ve en el próximo inicio de sesión; la baja o el bloqueo se aplican enseguida
    // (ver OnTokenValidated en Program.cs).
    public string GenerarToken(EmpleadoDto empleado, IEnumerable<string> roles, IEnumerable<string> permisos, TimeSpan? vida = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, empleado.Id.ToString()),
            new(ClaimTypes.Name, empleado.NombreCompleto),
            new(ClaimTypes.Email, empleado.Email)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permisos.Select(p => new Claim(ClaimsPermisos.Permiso, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(vida ?? TimeSpan.FromHours(12)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
