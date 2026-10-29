using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class ConexionTests : IDisposable
{
    private const string IdPlanilla = "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abc";

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_conexion_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true);
    }

    // Credencial de cuenta de servicio con una llave RSA real (el cliente de Google la parsea al conectar)
    private static string Credenciales(string correo = "fichaje@proyecto.iam.gserviceaccount.com") => JsonSerializer.Serialize(new
    {
        type = "service_account",
        project_id = "proyecto",
        private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        client_email = correo,
        token_uri = "https://oauth2.googleapis.com/token"
    });

    private AlmacenConexion NuevoAlmacen(string? clave = null) =>
        new(_carpeta, new EphemeralDataProtectionProvider(), clave);

    private class ProbadorFalso : IProbadorConexion
    {
        public string? Error { get; set; }
        public int Llamadas { get; private set; }
        public Task ProbarAsync(string spreadsheetId, string credencialesJson)
        {
            Llamadas++;
            return Error is null ? Task.CompletedTask : Task.FromException(new ConexionInvalidaException(Error));
        }
    }

    private class InicializadorFalso : IInicializadorPlanilla
    {
        public NuevoAdministrador? Recibido { get; private set; }
        public int Llamadas { get; private set; }
        public Task InicializarAsync(NuevoAdministrador? nuevo = null) { Llamadas++; Recibido = nuevo; return Task.CompletedTask; }
    }

    private (ConexionService Servicio, GoogleSheetsClient Cliente, AlmacenConexion Almacen, ProbadorFalso Probador, InicializadorFalso Inicializador) Armar()
    {
        var almacen = NuevoAlmacen("CLAVE123");
        var cliente = new GoogleSheetsClient(new ConfigurationBuilder().Build(), almacen, NullLogger<GoogleSheetsClient>.Instance);
        var probador = new ProbadorFalso();
        var inicializador = new InicializadorFalso();
        return (new ConexionService(cliente, almacen, probador, inicializador, NullLogger<ConexionService>.Instance),
            cliente, almacen, probador, inicializador);
    }

    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/" + IdPlanilla + "/edit#gid=0")]
    [InlineData("  " + IdPlanilla + "  ")]
    public void ExtraeElIdDeUnLinkOUnIdSuelto(string texto) =>
        Assert.Equal(IdPlanilla, ConexionService.ExtraerIdDePlanilla(texto));

    [Theory]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("https://example.com/otra-cosa")]
    public void UnTextoQueNoEsUnaPlanillaSeRechaza(string texto) =>
        Assert.Throws<ConexionInvalidaException>(() => ConexionService.ExtraerIdDePlanilla(texto));

    [Fact]
    public void SoloAceptaLaCredencialDeUnaCuentaDeServicio()
    {
        Assert.Equal("a@b.iam.gserviceaccount.com", ConexionService.ValidarCredenciales(Credenciales("a@b.iam.gserviceaccount.com")));

        Assert.Throws<ConexionInvalidaException>(() => ConexionService.ValidarCredenciales("no es json"));
        Assert.Throws<ConexionInvalidaException>(() => ConexionService.ValidarCredenciales("{\"type\":\"authorized_user\"}"));
        Assert.Throws<ConexionInvalidaException>(() => ConexionService.ValidarCredenciales(
            "{\"type\":\"authorized_user\",\"client_email\":\"x\",\"private_key\":\"y\"}"));
    }

    [Fact]
    public void SinConfigurar_LaConexionLoDice_YPedirLaPlanillaFalla()
    {
        var (_, cliente, _, _, _) = Armar();

        Assert.False(cliente.Configurado);
        Assert.Throws<ConexionNoConfiguradaException>(() => cliente.SpreadsheetId);
    }

    [Fact]
    public async Task Configurar_ProbarGuardarYConectar_YLaCredencialQuedaCifrada()
    {
        var (servicio, cliente, almacen, probador, inicializador) = Armar();
        var json = Credenciales();

        var correo = await servicio.ConfigurarAsync(new ConfigurarConexionRequest
        {
            Planilla = $"https://docs.google.com/spreadsheets/d/{IdPlanilla}/edit",
            Credenciales = json,
            NombreAdministrador = "Dueña",
            EmailAdministrador = "duena@cliente.com",
            PinAdministrador = "4321"
        });

        Assert.Equal("fichaje@proyecto.iam.gserviceaccount.com", correo);
        Assert.True(cliente.Configurado);
        Assert.Equal(IdPlanilla, cliente.SpreadsheetId);
        Assert.Equal(1, probador.Llamadas);
        Assert.Equal(new NuevoAdministrador("Dueña", "duena@cliente.com", "4321"), inicializador.Recibido);

        Assert.Equal(IdPlanilla, almacen.Leer()!.Value.SpreadsheetId);
        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(Path.Combine(_carpeta, "conexion.json")));
    }

    [Fact]
    public async Task SiLaPruebaFalla_NoSeGuardaNiSeConecta()
    {
        var (servicio, cliente, almacen, probador, inicializador) = Armar();
        probador.Error = "La planilla no está compartida con la cuenta de servicio.";

        var ex = await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(
            new ConfigurarConexionRequest { Planilla = IdPlanilla, Credenciales = Credenciales() }));

        Assert.Contains("compartida", ex.Message);
        Assert.False(cliente.Configurado);
        Assert.Null(almacen.Leer());
        Assert.Equal(0, inicializador.Llamadas);
    }

    [Theory]
    [InlineData("Dueña", "sin-arroba", "1234")]
    [InlineData("", "duena@cliente.com", "1234")]
    [InlineData("Dueña", "duena@cliente.com", "12")]
    [InlineData("Dueña", "duena@cliente.com", "abcd")]
    public async Task ElAdministradorInicialDebeSerValido(string nombre, string email, string pin)
    {
        var (servicio, cliente, _, probador, _) = Armar();

        await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(new ConfigurarConexionRequest
        {
            Planilla = IdPlanilla, Credenciales = Credenciales(),
            NombreAdministrador = nombre, EmailAdministrador = email, PinAdministrador = pin
        }));

        Assert.False(cliente.Configurado);
        Assert.Equal(0, probador.Llamadas); // se valida antes de ir a Google
    }

    [Fact]
    public async Task AlReiniciar_LaConexionGuardadaSeRecupera()
    {
        var (servicio, _, almacen, _, _) = Armar();
        await servicio.ConfigurarAsync(new ConfigurarConexionRequest { Planilla = IdPlanilla, Credenciales = Credenciales() });

        var otroArranque = new GoogleSheetsClient(new ConfigurationBuilder().Build(), almacen, NullLogger<GoogleSheetsClient>.Instance);

        Assert.True(otroArranque.Configurado);
        Assert.Equal(IdPlanilla, otroArranque.SpreadsheetId);
    }

    [Fact]
    public void LaClaveDeInstalacion_SeCompara_YSiNoSeDefineSeGeneraUnaYQuedaFija()
    {
        var (servicio, _, _, _, _) = Armar();
        Assert.True(servicio.ClaveCorrecta(" CLAVE123 "));
        Assert.False(servicio.ClaveCorrecta("otra"));
        Assert.False(servicio.ClaveCorrecta(null));

        var sinClave = NuevoAlmacen();
        var generada = sinClave.ClaveDeInstalacion();
        Assert.NotEmpty(generada);
        Assert.Equal(generada, NuevoAlmacen().ClaveDeInstalacion());
    }
}
