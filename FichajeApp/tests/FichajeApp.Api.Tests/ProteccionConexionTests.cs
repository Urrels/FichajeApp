using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Quién puede cambiar la planilla conectada: antes de configurar, quien tenga el código de activación;
// una vez configurada, solo un administrador con sesión (el código ya no alcanza).
public class ProteccionConexionTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_proteccion_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    private class ProbadorOk : IProbadorConexion { public Task ProbarAsync(string id, string json) => Task.CompletedTask; }
    private class InicializadorOk : IInicializadorPlanilla { public Task InicializarAsync(NuevoAdministrador? nuevo = null) => Task.CompletedTask; }

    private ConexionService Servicio()
    {
        var almacen = new AlmacenConexion(_carpeta, new EphemeralDataProtectionProvider(), "CODIGO");
        var cliente = new GoogleSheetsClient(new ConfigurationBuilder().Build(), almacen, NullLogger<GoogleSheetsClient>.Instance);
        return new ConexionService(cliente, almacen, new ProbadorOk(), new InicializadorOk(), NullLogger<ConexionService>.Instance);
    }

    private static string Credenciales() => JsonSerializer.Serialize(new
    {
        type = "service_account", project_id = "p", private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        client_email = "f@p.iam.gserviceaccount.com", token_uri = "https://oauth2.googleapis.com/token"
    });

    private static Task Conectar(ConexionService s) => s.ConfigurarAsync(new ConfigurarConexionRequest
    {
        Planilla = "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abc", Credenciales = Credenciales()
    });

    [Fact]
    public void SinConfigurar_AlcanzaConElCodigo_AunqueNoHayaAdministrador()
    {
        var s = Servicio();

        Assert.Equal(ConexionService.Acceso.Permitido, s.VerificarAcceso(esAdministrador: false, "CODIGO"));
        Assert.Equal(ConexionService.Acceso.ClaveIncorrecta, s.VerificarAcceso(false, "otro"));
        Assert.Equal(ConexionService.Acceso.ClaveIncorrecta, s.VerificarAcceso(false, null));
    }

    [Fact]
    public void SinConfigurar_UnAdministradorSinCodigoNoPasa()
    {
        // No puede existir un administrador antes de configurar: sin código no hay acceso
        Assert.Equal(ConexionService.Acceso.ClaveIncorrecta, Servicio().VerificarAcceso(esAdministrador: true, clave: null));
    }

    [Fact]
    public async Task YaConfigurada_ElCodigoYaNoAlcanza_SoloUnAdministrador()
    {
        var s = Servicio();
        await Conectar(s);

        Assert.Equal(ConexionService.Acceso.SoloAdministrador, s.VerificarAcceso(esAdministrador: false, "CODIGO"));
        Assert.Equal(ConexionService.Acceso.SoloAdministrador, s.VerificarAcceso(false, null));
        Assert.Equal(ConexionService.Acceso.Permitido, s.VerificarAcceso(esAdministrador: true, clave: null));
    }

    [Fact]
    public void ElPermisoDeCambiarLaConexion_ExisteEnElCatalogo_YLoTieneElAdministrador_PeroNoElEmpleado()
    {
        Assert.Contains(CatalogoPermisos.CambiarConexion, CatalogoPermisos.Todos);
        Assert.Contains(CatalogoPermisos.CambiarConexion, CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolAdministrador]);
        Assert.DoesNotContain(CatalogoPermisos.CambiarConexion, CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolEmpleado]);
        Assert.DoesNotContain(CatalogoPermisos.CambiarConexion, CatalogoPermisos.RolesIniciales[CatalogoPermisos.RolSupervisor]);
        Assert.DoesNotContain(CatalogoPermisos.CambiarConexion, CatalogoPermisos.PermisosDelRolKiosco);
    }
}
