using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Si la preparación de una planilla nueva falla a la mitad, no puede quedar "conectada pero a medio hacer":
// el código de activación ya no alcanzaría para reintentar. Se deshace y se puede volver a intentar.
public class PreparacionDePlanillaTests : IDisposable
{
    private const string Planilla1 = "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-aaa";
    private const string Planilla2 = "1AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-bbb";

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_prep_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    private class ProbadorOk : IProbadorConexion { public Task ProbarAsync(string id, string json) => Task.CompletedTask; }

    private class InicializadorQueFalla : IInicializadorPlanilla
    {
        public Exception? Error { get; set; }
        public int Llamadas { get; private set; }
        public Task InicializarAsync(NuevoAdministrador? nuevo = null)
        {
            Llamadas++;
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }
    }

    private static string Credenciales() => JsonSerializer.Serialize(new
    {
        type = "service_account", project_id = "p", private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        client_email = "f@p.iam.gserviceaccount.com", token_uri = "https://oauth2.googleapis.com/token"
    });

    private (ConexionService Servicio, GoogleSheetsClient Cliente, AlmacenConexion Almacen, InicializadorQueFalla Inicializador) Armar()
    {
        var almacen = new AlmacenConexion(_carpeta, new EphemeralDataProtectionProvider(), "CODIGO");
        var cliente = new GoogleSheetsClient(new ConfigurationBuilder().Build(), almacen, NullLogger<GoogleSheetsClient>.Instance);
        var inicializador = new InicializadorQueFalla();
        return (new ConexionService(cliente, almacen, new ProbadorOk(), inicializador, new CreadorFalso(), NullLogger<ConexionService>.Instance),
            cliente, almacen, inicializador);
    }

    private static ConfigurarConexionRequest Pedido(string planilla) =>
        new() { Planilla = planilla, Credenciales = Credenciales() };

    [Fact]
    public async Task SiFallaLaPreparacion_NoQuedaNadaGuardadoNiConectado_YSePuedeReintentarConElMismoCodigo()
    {
        var (servicio, cliente, almacen, inicializador) = Armar();
        inicializador.Error = new InvalidOperationException("se cortó");

        var ex = await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Pedido(Planilla1)));

        Assert.Contains("no se pudo prepararla", ex.Message);
        Assert.False(cliente.Configurado);
        Assert.Null(almacen.Leer());
        // Reintentar: sigue valiendo el código de activación (no quedó "ya conectada")
        Assert.Equal(ConexionService.Acceso.Permitido, await servicio.VerificarAccesoAsync(esAdministrador: false, "CODIGO"));

        inicializador.Error = null;
        await servicio.ConfigurarAsync(Pedido(Planilla1));
        Assert.True(cliente.Configurado);
        Assert.Equal(2, inicializador.Llamadas);
    }

    [Fact]
    public async Task SiUnAdministradorCambiaDePlanilla_YFalla_SeConservaLaConexionAnterior()
    {
        var (servicio, cliente, almacen, inicializador) = Armar();
        await servicio.ConfigurarAsync(Pedido(Planilla1));
        Assert.Equal(Planilla1, cliente.SpreadsheetId);

        inicializador.Error = new InvalidOperationException("se cortó");
        await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Pedido(Planilla2)));

        Assert.True(cliente.Configurado);
        Assert.Equal(Planilla1, cliente.SpreadsheetId);            // sigue conectada a la de antes
        Assert.Equal(Planilla1, almacen.Leer()!.Value.SpreadsheetId); // y así queda guardada para el próximo arranque
    }

    [Fact]
    public async Task ElMensajeDeError_NoMuestraDetallesInternos_SiGoogleLimitaLosPedidos()
    {
        var (servicio, _, _, inicializador) = Armar();
        inicializador.Error = new Google.GoogleApiException("Sheets", "Quota exceeded") { HttpStatusCode = HttpStatusCode.TooManyRequests };

        var ex = await Assert.ThrowsAsync<ConexionInvalidaException>(() => servicio.ConfigurarAsync(Pedido(Planilla1)));

        Assert.Contains("limitó los pedidos por minuto", ex.Message);
    }
}

// Google responde 429 cuando se pasan los pedidos por minuto: se espera y se reintenta en vez de fallar
public class ReintentosPorCuotaTests
{
    private class RespuestasEnOrden : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _codigos;
        public int Llamadas { get; private set; }
        public RespuestasEnOrden(params HttpStatusCode[] codigos) => _codigos = new Queue<HttpStatusCode>(codigos);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            var codigo = _codigos.Count > 1 ? _codigos.Dequeue() : _codigos.Peek(); // la última se repite
            return Task.FromResult(new HttpResponseMessage(codigo));
        }
    }

    private class SinCredencial : IConfigurableHttpClientInitializer { public void Initialize(ConfigurableHttpClient httpClient) { } }

    private static (ConfigurableHttpClient Cliente, RespuestasEnOrden Servidor) Armar(params HttpStatusCode[] codigos)
    {
        var servidor = new RespuestasEnOrden(codigos);
        var cliente = new ConfigurableHttpClient(new ConfigurableMessageHandler(servidor));
        new ReintentosPorCuota(new SinCredencial(), TimeSpan.FromMilliseconds(100), reintentos: 4).Initialize(cliente);
        return (cliente, servidor);
    }

    [Fact]
    public async Task UnLimiteDeCuota_SeReintentaHastaQueGooglePermite()
    {
        var (cliente, servidor) = Armar(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var respuesta = await cliente.GetAsync("http://sheets.test/algo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(3, servidor.Llamadas);
    }

    [Fact]
    public async Task UnPermisoDenegado_NoSeReintenta_PorqueNoVaASolucionarseEsperando()
    {
        var (cliente, servidor) = Armar(HttpStatusCode.Forbidden);

        var respuesta = await cliente.GetAsync("http://sheets.test/algo");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(1, servidor.Llamadas);
    }

    [Fact]
    public async Task SiLaCuotaNuncaSeLibera_SeRindeDespuesDeVariosIntentos_SinQuedarseColgado()
    {
        var (cliente, servidor) = Armar(HttpStatusCode.TooManyRequests);

        var respuesta = await cliente.GetAsync("http://sheets.test/algo");

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.InRange(servidor.Llamadas, 2, 5);
    }

    [Fact]
    public async Task UnPedidoQueAndaALaPrimera_NoEspera()
    {
        var (cliente, servidor) = Armar(HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("http://sheets.test/algo")).StatusCode);
        Assert.Equal(1, servidor.Llamadas);
    }
}
