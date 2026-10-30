using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// "Todo el proceso a la vez": el servidor crea la planilla nueva, la prepara y se la comparte al cliente.
// Y si la planilla conectada se borra, la app lo detecta y deja volver a conectar (antes quedaba trabada).
public class PlanillaNuevaTests : IDisposable
{
    private const string PlanillaVieja = "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-old";

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_nueva_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    private class ProbadorControlable : IProbadorConexion
    {
        public Exception? Falla { get; set; }
        public int Llamadas { get; private set; }
        public Task ProbarAsync(string id, string json)
        {
            Llamadas++;
            return Falla is null ? Task.CompletedTask : Task.FromException(Falla);
        }
    }

    private class InicializadorEspia : IInicializadorPlanilla
    {
        public Exception? Falla { get; set; }
        public NuevoAdministrador? Recibido { get; private set; }
        public int Llamadas { get; private set; }
        public Task InicializarAsync(NuevoAdministrador? nuevo = null)
        {
            Llamadas++; Recibido = nuevo;
            return Falla is null ? Task.CompletedTask : Task.FromException(Falla);
        }
    }

    private static string Credenciales() => JsonSerializer.Serialize(new
    {
        type = "service_account", project_id = "p", private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        client_email = "servicio@p.iam.gserviceaccount.com", token_uri = "https://oauth2.googleapis.com/token"
    });

    private (ConexionService Servicio, GoogleSheetsClient Cliente, AlmacenConexion Almacen, ProbadorControlable Probador,
        InicializadorEspia Inicializador, CreadorFalso Creador, RelojFalso Reloj) Armar()
    {
        var almacen = new AlmacenConexion(_carpeta, new EphemeralDataProtectionProvider(), "CODIGO");
        var cliente = new GoogleSheetsClient(new ConfigurationBuilder().Build(), almacen, NullLogger<GoogleSheetsClient>.Instance);
        var probador = new ProbadorControlable();
        var inicializador = new InicializadorEspia();
        var creador = new CreadorFalso();
        var reloj = new RelojFalso();
        return (new ConexionService(cliente, almacen, probador, inicializador, creador, NullLogger<ConexionService>.Instance, reloj),
            cliente, almacen, probador, inicializador, creador, reloj);
    }

    private static ConfigurarConexionRequest Nueva(Action<ConfigurarConexionRequest>? ajustar = null)
    {
        var r = new ConfigurarConexionRequest
        {
            Credenciales = Credenciales(), CrearPlanillaNueva = true, EmailPropietario = "duena@gmail.com",
            NombreEmpresa = "Gimnasio Sol", NombreAdministrador = "Ana Dueña", PinAdministrador = "1234"
        };
        ajustar?.Invoke(r);
        return r;
    }

    // ---- crear la planilla ----

    [Fact]
    public async Task CreaLaPlanilla_LaConecta_LaPrepara_YDevuelveElLink()
    {
        var (servicio, cliente, almacen, probador, inicializador, creador, _) = Armar();

        var r = await servicio.ConfigurarAsync(Nueva());

        Assert.Equal(("Fichaje - Gimnasio Sol", "duena@gmail.com"), Assert.Single(creador.Creadas));
        Assert.Equal(creador.IdQueDevuelve, cliente.SpreadsheetId);
        Assert.Equal(creador.IdQueDevuelve, almacen.Leer()!.Value.SpreadsheetId);
        Assert.Equal(0, probador.Llamadas); // recién creada: no hace falta probarla
        Assert.Equal(1, inicializador.Llamadas);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{creador.IdQueDevuelve}", r.PlanillaCreadaUrl);
        Assert.Equal("Fichaje - Gimnasio Sol", r.PlanillaCreadaTitulo);
        Assert.Equal("duena@gmail.com", r.CompartidaCon);
    }

    [Fact]
    public async Task ElAdministradorSinEmail_UsaElDelPropietario()
    {
        var (servicio, _, _, _, inicializador, _, _) = Armar();

        await servicio.ConfigurarAsync(Nueva());

        Assert.Equal(new NuevoAdministrador("Ana Dueña", "duena@gmail.com", "1234"), inicializador.Recibido);
    }

    [Fact]
    public async Task UnEmailDeAdministradorPropio_SeRespeta()
    {
        var (servicio, _, _, _, inicializador, _, _) = Armar();

        await servicio.ConfigurarAsync(Nueva(r => r.EmailAdministrador = "admin@empresa.com"));

        Assert.Equal("admin@empresa.com", inicializador.Recibido!.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("con espacio@gmail.com")]
    public async Task SinUnEmailDeGoogleValido_NoCreaNada(string email)
    {
        var (servicio, cliente, _, _, _, creador, _) = Armar();

        await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Nueva(r => r.EmailPropietario = email)));

        Assert.Empty(creador.Creadas);
        Assert.False(cliente.Configurado);
    }

    [Theory]
    [InlineData("", "1234")]
    [InlineData("Ana", "12")]
    [InlineData("Ana", "")]
    public async Task ConPlanillaNueva_ElAdministradorEsObligatorioYValido_ANTESdeCrear(string nombre, string pin)
    {
        var (servicio, _, _, _, _, creador, _) = Armar();

        await Assert.ThrowsAsync<ConexionInvalidaException>(() =>
            servicio.ConfigurarAsync(Nueva(r => { r.NombreAdministrador = nombre; r.PinAdministrador = pin; })));

        Assert.Empty(creador.Creadas); // no se deja una planilla huérfana por un dato mal puesto
    }

    [Theory]
    [InlineData("Gimnasio Sol", "Ana", "Fichaje - Gimnasio Sol")]
    [InlineData(null, "Ana Dueña", "Fichaje - Ana Dueña")]
    [InlineData("  ", "Ana Dueña", "Fichaje - Ana Dueña")]
    public async Task ElTituloUsaElNombreDeLaEmpresa_OSiNoElDelAdministrador(string? empresa, string admin, string esperado)
    {
        var (servicio, _, _, _, _, creador, _) = Armar();

        await servicio.ConfigurarAsync(Nueva(r => { r.NombreEmpresa = empresa; r.NombreAdministrador = admin; }));

        Assert.Equal(esperado, creador.Creadas.Single().Titulo);
    }

    [Fact]
    public async Task SiGoogleNoPuedeCrearla_ElMensajeLlegaTalCual_YNoQuedaNadaGuardado()
    {
        var (servicio, cliente, almacen, _, inicializador, creador, _) = Armar();
        creador.ErrorAlCrear = new ConexionInvalidaException("La API de Google Drive no está activada.");

        var ex = await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Nueva()));

        Assert.Contains("Drive", ex.Message);
        Assert.False(cliente.Configurado);
        Assert.Null(almacen.Leer());
        Assert.Equal(0, inicializador.Llamadas);
    }

    [Fact]
    public async Task SiFallaLaPreparacion_SeDeshaceTodo_YSeBorraLaPlanillaRecienCreada()
    {
        var (servicio, cliente, almacen, _, inicializador, creador, _) = Armar();
        inicializador.Falla = new InvalidOperationException("se cortó");

        await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Nueva()));

        Assert.False(cliente.Configurado);
        Assert.Null(almacen.Leer());
        Assert.Equal(new[] { creador.IdQueDevuelve }, creador.Eliminadas); // no queda una planilla huérfana en el Drive
        Assert.Equal(ConexionService.Acceso.Permitido, await servicio.VerificarAccesoAsync(false, "CODIGO")); // se puede reintentar
    }

    [Fact]
    public async Task ElCaminoDeSiempre_ConUnLink_NoCreaNada()
    {
        var (servicio, _, _, probador, _, creador, _) = Armar();

        var r = await servicio.ConfigurarAsync(new ConfigurarConexionRequest { Planilla = PlanillaVieja, Credenciales = Credenciales() });

        Assert.Empty(creador.Creadas);
        Assert.Equal(1, probador.Llamadas);
        Assert.Null(r.PlanillaCreadaUrl);
    }

    // ---- planilla que se borró ----

    [Fact]
    public async Task SinPlanillaConectada_NoEstaLista_YNoSeConsultaAGoogle()
    {
        var (servicio, _, _, probador, _, _, _) = Armar();

        var e = await servicio.EstadoAsync();

        Assert.False(e.Conectada);
        Assert.False(e.Lista);
        Assert.Equal(0, probador.Llamadas);
    }

    [Fact]
    public async Task ConPlanillaQueExiste_EstaLista()
    {
        var (servicio, _, _, _, _, _, _) = Armar();
        await servicio.ConfigurarAsync(Nueva());

        var e = await servicio.EstadoAsync();

        Assert.True(e.Lista);
        Assert.Null(e.Aviso);
    }

    [Fact]
    public async Task SiLaPlanillaSeBorro_NoEstaLista_ConUnAvisoQueLoExplica()
    {
        var (servicio, _, _, probador, _, _, reloj) = Armar();
        await servicio.ConfigurarAsync(Nueva());

        probador.Falla = new ConexionInvalidaException("No se encontró la planilla. Revisá el link o el ID (puede que se haya borrado).");
        reloj.Ahora = reloj.Ahora.AddMinutes(1); // pasó el control anterior
        var e = await servicio.EstadoAsync();

        Assert.True(e.Conectada);
        Assert.False(e.Accesible);
        Assert.False(e.Lista);
        Assert.Contains("ya no está disponible", e.Aviso);
        Assert.Contains("se haya borrado", e.Aviso);
    }

    [Fact]
    public async Task ConLaPlanillaBorrada_VuelveAAlcanzarElCodigo_ParaPoderConectarOtra()
    {
        var (servicio, _, _, probador, _, _, reloj) = Armar();
        await servicio.ConfigurarAsync(Nueva());
        probador.Falla = new ConexionInvalidaException("No se encontró la planilla.");
        reloj.Ahora = reloj.Ahora.AddMinutes(1);

        // Los administradores vivían en la planilla perdida: sin esto nadie podría reconectar nunca
        Assert.Equal(ConexionService.Acceso.Permitido, await servicio.VerificarAccesoAsync(esAdministrador: false, "CODIGO"));
        Assert.Equal(ConexionService.Acceso.ClaveIncorrecta, await servicio.VerificarAccesoAsync(false, "otro"));
    }

    [Fact]
    public async Task UnErrorDeRed_NoSeToma_PorPlanillaBorrada()
    {
        var (servicio, _, _, probador, _, _, reloj) = Armar();
        await servicio.ConfigurarAsync(Nueva());
        probador.Falla = new HttpRequestException("sin internet");
        reloj.Ahora = reloj.Ahora.AddMinutes(1);

        var e = await servicio.EstadoAsync();

        Assert.True(e.Lista); // si Google no responde no se puede saber: no se deja a nadie afuera
    }

    [Fact]
    public async Task ElControlSeRepiteComoMuchoCada20Segundos()
    {
        var (servicio, _, _, probador, _, _, reloj) = Armar();
        await servicio.ConfigurarAsync(Nueva());

        await servicio.EstadoAsync();
        var tras = probador.Llamadas;
        await servicio.EstadoAsync();
        await servicio.EstadoAsync();
        Assert.Equal(tras, probador.Llamadas); // dentro de los 20 s no vuelve a preguntarle a Google

        reloj.Ahora = reloj.Ahora.AddSeconds(21);
        await servicio.EstadoAsync();
        Assert.Equal(tras + 1, probador.Llamadas);
    }

    [Fact]
    public async Task AlConectarOtraPlanilla_ElEstadoViejoSeOlvida()
    {
        var (servicio, _, _, probador, _, _, reloj) = Armar();
        await servicio.ConfigurarAsync(Nueva());
        probador.Falla = new ConexionInvalidaException("No se encontró la planilla.");
        reloj.Ahora = reloj.Ahora.AddMinutes(1);
        Assert.False((await servicio.EstadoAsync()).Lista);

        probador.Falla = null; // la nueva sí existe
        await servicio.ConfigurarAsync(new ConfigurarConexionRequest { Planilla = PlanillaVieja, Credenciales = Credenciales() });

        Assert.True((await servicio.EstadoAsync()).Lista);
    }
}
