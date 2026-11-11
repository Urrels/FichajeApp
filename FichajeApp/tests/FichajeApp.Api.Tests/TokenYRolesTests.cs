using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Tests;

public class JwtServiceTests
{
    private const string Secreto = "una-clave-de-prueba-larga-de-al-menos-32-caracteres";

    private static JwtService Servicio(string secreto = Secreto) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secreto, ["Jwt:Issuer"] = "FichajeApp" }).Build());

    private static readonly EmpleadoDto Ana = new() { Id = 7, NombreCompleto = "Ana Empleada", Email = "ana@test.com", PinHash = "x", Activo = true };

    private static ClaimsPrincipal Validar(string token, string secreto = Secreto) =>
        new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "FichajeApp", ValidAudience = "FichajeApp",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto)),
            ClockSkew = TimeSpan.Zero
        }, out _);

    [Fact]
    public void ElTokenLlevaIdentidad_Roles_YPermisos()
    {
        var token = Servicio().GenerarToken(Ana, new[] { "Empleado" }, new[] { "Fichar asistencia", "Cambiar PIN" });
        var p = Validar(token);

        Assert.Equal("7", p.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal("Ana Empleada", p.FindFirst(ClaimTypes.Name)!.Value);
        Assert.Equal("ana@test.com", p.FindFirst(ClaimTypes.Email)!.Value);
        Assert.Equal(new[] { "Empleado" }, p.FindAll(ClaimTypes.Role).Select(c => c.Value));
        Assert.Equal(new[] { "Fichar asistencia", "Cambiar PIN" }, p.FindAll(ClaimsPermisos.Permiso).Select(c => c.Value));
    }

    [Fact]
    public void PorDefectoDuraDoceHoras_YSePuedeAlargar()
    {
        var normal = new JwtSecurityTokenHandler().ReadJwtToken(Servicio().GenerarToken(Ana, Array.Empty<string>(), Array.Empty<string>()));
        var largo = new JwtSecurityTokenHandler().ReadJwtToken(Servicio().GenerarToken(Ana, Array.Empty<string>(), Array.Empty<string>(), TimeSpan.FromDays(30)));

        Assert.InRange((normal.ValidTo - DateTime.UtcNow).TotalHours, 11.9, 12.1);
        Assert.InRange((largo.ValidTo - DateTime.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public void UnTokenFirmadoConOtraClave_NoSeAcepta()
    {
        var token = Servicio("otra-clave-distinta-de-al-menos-32-caracteres!!").GenerarToken(Ana, Array.Empty<string>(), Array.Empty<string>());

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() => Validar(token));
    }

    [Fact]
    public void SinRolesNiPermisos_ElTokenIgualSeGenera()
    {
        var p = Validar(Servicio().GenerarToken(Ana, Array.Empty<string>(), Array.Empty<string>()));

        Assert.Empty(p.FindAll(ClaimTypes.Role));
        Assert.Empty(p.FindAll(ClaimsPermisos.Permiso));
    }
}

public class RolesEfectivosTests
{
    private static readonly List<Rol> Roles = new()
    {
        new Rol { Id = 1, Nombre = "Administrador" }, new Rol { Id = 2, Nombre = "Supervisor" },
        new Rol { Id = 3, Nombre = "Empleado" }, new Rol { Id = 4, Nombre = "Kiosco" }
    };

    private static EmpleadoDto Persona(string rolInicial) =>
        new() { Id = 10, NombreCompleto = "X", Email = "x@x.com", PinHash = "x", Activo = true, Rol = rolInicial };

    private static readonly Dictionary<int, List<int>> SinAsignaciones = new();

    [Theory]
    [InlineData("Administrador", 1)]
    [InlineData("administrador", 1)] // no distingue mayúsculas
    [InlineData("Supervisor", 2)]
    [InlineData("", 3)]
    [InlineData("Cualquier cosa", 3)]
    public void SinAsignaciones_ManejaElRolInicialDeLaColumnaF(string rolInicial, int idEsperado) =>
        Assert.Equal(new[] { idEsperado }, PermisosService.RolesEfectivos(Persona(rolInicial), SinAsignaciones, Roles));

    [Fact]
    public void LasAsignacionesGananSobreLaColumnaF()
    {
        var asignaciones = new Dictionary<int, List<int>> { [10] = new() { 4 } };

        Assert.Equal(new[] { 4 }, PermisosService.RolesEfectivos(Persona("Administrador"), asignaciones, Roles));
    }

    [Fact]
    public void UnaAsignacionVacia_VuelveAlRolInicial()
    {
        var asignaciones = new Dictionary<int, List<int>> { [10] = new() };

        Assert.Equal(new[] { 2 }, PermisosService.RolesEfectivos(Persona("Supervisor"), asignaciones, Roles));
    }

    [Fact]
    public void SiElRolInicialNoExisteEnLaPlanilla_NoTieneRoles() =>
        Assert.Empty(PermisosService.RolesEfectivos(Persona("Supervisor"), SinAsignaciones, new List<Rol> { new() { Id = 9, Nombre = "Otro" } }));

    [Fact]
    public void ElRolKiosco_NoPuedeFicharPorSiMismo_PeroElAdministradorTieneTodo()
    {
        Assert.DoesNotContain(CatalogoPermisos.FicharAsistencia, CatalogoPermisos.PermisosDelRolKiosco);
        Assert.Contains(CatalogoPermisos.FicharEnKiosco, CatalogoPermisos.PermisosDelRolKiosco);
        Assert.Equal(CatalogoPermisos.Todos, CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolAdministrador]);
    }

    [Fact]
    public void ElCatalogoNoRepitePermisos_YTodoRolInicialUsaSoloPermisosDelCatalogo()
    {
        Assert.Equal(CatalogoPermisos.Todos.Length, CatalogoPermisos.Todos.Distinct().Count());
        Assert.All(CatalogoPermisos.RolesIniciales.Values.SelectMany(p => p), p => Assert.Contains(p, CatalogoPermisos.Todos));
    }
}

