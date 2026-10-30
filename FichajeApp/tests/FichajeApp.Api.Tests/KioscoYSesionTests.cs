using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

// Error que se encontró probando: el Administrador tiene TODOS los permisos (incluido "Fichar en kiosco"), así que la app
// lo mandaba a la pantalla del kiosco y no veía su panel. Una cuenta de sucursal es la que puede usar el kiosco y NO ficha.
public class EsSoloKioscoTests
{
    [Fact]
    public void LaCuentaDeSucursal_SiEsSoloKiosco()
    {
        Assert.True(CatalogoPermisos.EsSoloKiosco(CatalogoPermisos.PermisosDelRolKiosco));
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Supervisor")]
    [InlineData("Empleado")]
    public void NingunOtroRolEsUnaCuentaDeSucursal(string rol) =>
        Assert.False(CatalogoPermisos.EsSoloKiosco(CatalogoPermisos.RolesIniciales[rol]));

    [Fact]
    public void SinPermisosNoEsKiosco() => Assert.False(CatalogoPermisos.EsSoloKiosco(Array.Empty<string>()));

    [Fact]
    public void ElAdministradorTieneElPermisoDelKiosco_PeroTambienElDeFichar()
    {
        var admin = CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolAdministrador];

        Assert.Contains(CatalogoPermisos.FicharEnKiosco, admin);
        Assert.Contains(CatalogoPermisos.FicharAsistencia, admin);
    }
}

public class SesionDelAdministradorPorHttpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public SesionDelAdministradorPorHttpTests(ApiFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Datos.AgregarSucursal();
    }

    private async Task<(JsonElement Json, JwtSecurityToken Token)> Login(string email, string pin)
    {
        var r = await _fabrica.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (json, new JwtSecurityTokenHandler().ReadJwtToken(json.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task ElAdministradorRecibeUnaSesionNormal_DeDoceHoras_YNoLaDe30DiasDelKiosco()
    {
        var (json, token) = await Login("admin@test.com", Entorno.PinAdmin);

        Assert.True(token.ValidTo < DateTime.UtcNow.AddHours(13));
        Assert.Contains(json.GetProperty("permisos").EnumerateArray(), p => p.GetString() == CatalogoPermisos.FicharEnKiosco);
        Assert.Contains(json.GetProperty("permisos").EnumerateArray(), p => p.GetString() == CatalogoPermisos.VerPanelSupervisor);
    }

    [Fact]
    public async Task LaCuentaDeSucursalSiRecibeLaSesionLarga()
    {
        var (_, token) = await Login("centro@test.com", Entorno.PinKiosco);

        Assert.True(token.ValidTo > DateTime.UtcNow.AddDays(29));
    }
}

// La misma regla en la app (SessionManager.EsKiosco y a qué pantalla va cada usuario al entrar)
public class NavegacionDeLaAppTests
{
    private static void Entrar(params string[] permisos)
    {
        var s = SessionManager.Instance;
        s.FinalizarSesion();
        s.IniciarSesion("token", "Nombre", 1, "Rol", permisos);
    }

    [Fact]
    public void ElAdministradorVaASuPanel_NoAlKiosco()
    {
        Entrar(CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolAdministrador]);

        Assert.False(SessionManager.Instance.EsKiosco);
        Assert.True(SessionManager.Instance.EsAdministrador);
        Assert.Equal("//SupervisorPage", Navegacion.RutaInicial()); // tiene el permiso de supervisor; desde ahí llega a Administración
    }

    [Fact]
    public void LaCuentaDeSucursalVaAlKiosco()
    {
        Entrar(CatalogoPermisos.PermisosDelRolKiosco);

        Assert.True(SessionManager.Instance.EsKiosco);
        Assert.Equal("//KioscoPage", Navegacion.RutaInicial());
    }

    [Theory]
    [InlineData("Supervisor", "//SupervisorPage")]
    [InlineData("Empleado", "//FichajePage")]
    public void CadaRolVaASuPantalla(string rol, string esperada)
    {
        Entrar(CatalogoPermisos.RolesIniciales[rol]);

        Assert.False(SessionManager.Instance.EsKiosco);
        Assert.Equal(esperada, Navegacion.RutaInicial());
    }

    [Fact]
    public void UnAdministradorSinPermisoDeSupervisor_VaALaAdministracion()
    {
        Entrar(CatalogoPermisos.AdministrarUsuarios, CatalogoPermisos.FicharAsistencia);

        Assert.Equal("//AdminPage", Navegacion.RutaInicial());
    }

    [Fact]
    public void SinSesion_NoEsNadaYVaAFichar()
    {
        SessionManager.Instance.FinalizarSesion();

        Assert.False(SessionManager.Instance.EstaLogueado);
        Assert.False(SessionManager.Instance.EsKiosco);
        Assert.False(SessionManager.Instance.EsAdministrador);
        Assert.Equal("//FichajePage", Navegacion.RutaInicial());
    }

    [Fact]
    public void LosNombresDePermisosDeLaApp_CoincidenConLosDelCatalogoDelServidor()
    {
        // Si alguien cambia un nombre de un lado y no del otro, la app deja de ver los botones sin dar error
        Assert.Equal(CatalogoPermisos.FicharEnKiosco, NombresPermisos.FicharEnKiosco);
        Assert.Equal(CatalogoPermisos.FicharAsistencia, NombresPermisos.FicharAsistencia);
        Assert.Equal(CatalogoPermisos.VerPanelSupervisor, NombresPermisos.VerPanelSupervisor);
        Assert.Equal(CatalogoPermisos.ConfigurarFichaje, NombresPermisos.ConfigurarFichaje);
        Assert.Equal(CatalogoPermisos.CambiarConexion, NombresPermisos.CambiarConexion);
        Assert.Equal(CatalogoPermisos.GestionarApariencia, NombresPermisos.GestionarApariencia);
        Assert.Equal(CatalogoPermisos.AdministrarUsuarios, NombresPermisos.AdministrarUsuarios);
        Assert.All(NombresPermisos.DeAdministracion, p => Assert.Contains(p, CatalogoPermisos.Todos));
    }
}
