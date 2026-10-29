using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google;
using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Servicios;

public class ConfigurarConexionRequest
{
    public string? Clave { get; set; }
    public string? Planilla { get; set; }
    public string? Credenciales { get; set; }
    public string? NombreAdministrador { get; set; }
    public string? EmailAdministrador { get; set; }
    public string? PinAdministrador { get; set; }
}

public class ConexionInvalidaException : Exception
{
    public ConexionInvalidaException(string mensaje) : base(mensaje) { }
}

// Comprueba contra Google que la credencial puede leer y escribir la planilla (se reemplaza en los tests)
public interface IProbadorConexion
{
    Task ProbarAsync(string spreadsheetId, string credencialesJson);
}

public class ProbadorConexionGoogle : IProbadorConexion
{
    public async Task ProbarAsync(string spreadsheetId, string credencialesJson)
    {
        var (servicio, correo) = GoogleSheetsClient.CrearServicio(credencialesJson);
        try
        {
            var pedido = servicio.Spreadsheets.Get(spreadsheetId);
            pedido.Fields = "spreadsheetId,properties.title";
            await pedido.ExecuteAsync();
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ConexionInvalidaException("No se encontró la planilla. Revisá el link o el ID.");
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new ConexionInvalidaException(ex.Message.Contains("has not been used", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                ? "La API de Google Sheets no está activada en el proyecto de Google Cloud de esta credencial. Activala en 'APIs y servicios' → 'Google Sheets API'."
                : $"La planilla no está compartida con la cuenta de servicio. Compartila como Editor con: {correo}");
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new ConexionInvalidaException("Ese archivo no es una planilla de Google Sheets (si es un Excel, abrilo y usá Archivo → Guardar como Hoja de cálculo de Google).");
        }
        catch (Exception ex) when (ex is Google.Apis.Auth.OAuth2.Responses.TokenResponseException or InvalidOperationException)
        {
            throw new ConexionInvalidaException("Google no aceptó la credencial. Descargá de nuevo el archivo JSON de la cuenta de servicio.");
        }
    }
}

// Configuración de la conexión con Google Sheets desde la app: el cliente carga su planilla y la credencial de su
// cuenta de servicio, se prueba contra Google y se guarda cifrada en el servidor.
public class ConexionService
{
    private static readonly Regex IdEnLink = new(@"/spreadsheets/d/([a-zA-Z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex IdSuelto = new(@"^[a-zA-Z0-9_-]{20,}$", RegexOptions.Compiled);
    private static readonly Regex PinValido = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly SemaphoreSlim _configuracion = new(1, 1);

    private readonly GoogleSheetsClient _cliente;
    private readonly AlmacenConexion _almacen;
    private readonly IProbadorConexion _probador;
    private readonly IInicializadorPlanilla _inicializador;
    private readonly ILogger<ConexionService> _log;

    public ConexionService(GoogleSheetsClient cliente, AlmacenConexion almacen, IProbadorConexion probador,
        IInicializadorPlanilla inicializador, ILogger<ConexionService> log)
    {
        _cliente = cliente;
        _almacen = almacen;
        _probador = probador;
        _inicializador = inicializador;
        _log = log;
    }

    public bool Configurada => _cliente.Configurado;

    // Correo con el que hay que compartir la planilla; null si el servidor no tiene credencial (se carga desde la app)
    public string? CuentaDeServicio => _cliente.CorreoCuentaServicio;

    public bool ClaveCorrecta(string? clave)
    {
        var esperada = Encoding.UTF8.GetBytes(_almacen.ClaveDeInstalacion());
        var recibida = Encoding.UTF8.GetBytes((clave ?? "").Trim());
        return CryptographicOperations.FixedTimeEquals(esperada, recibida);
    }

    // Sale en el log al arrancar sin conexión, para que quien instala el servidor sepa qué clave poner en la app
    public void AnunciarClave() =>
        _log.LogWarning("Conexión con Google Sheets sin configurar. Código de activación para la app: {Clave}", _almacen.ClaveDeInstalacion());

    public static string ExtraerIdDePlanilla(string? texto)
    {
        var t = (texto ?? "").Trim();
        var enLink = IdEnLink.Match(t);
        if (enLink.Success) return enLink.Groups[1].Value;
        if (IdSuelto.IsMatch(t)) return t;
        throw new ConexionInvalidaException("Pegá el link de la planilla (docs.google.com/spreadsheets/d/...) o su ID.");
    }

    // Devuelve el correo de la cuenta de servicio si el JSON es válido
    public static string ValidarCredenciales(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            var raiz = doc.RootElement;
            var tipo = raiz.GetProperty("type").GetString();
            var correo = raiz.GetProperty("client_email").GetString();
            var clave = raiz.GetProperty("private_key").GetString();
            if (tipo != "service_account" || string.IsNullOrEmpty(correo) || string.IsNullOrEmpty(clave))
                throw new ConexionInvalidaException("El archivo no es la credencial de una cuenta de servicio.");
            return correo;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ConexionInvalidaException("Las credenciales no son válidas: pegá el contenido completo del archivo .json de la cuenta de servicio.");
        }
    }

    public async Task<string> ConfigurarAsync(ConfigurarConexionRequest r)
    {
        var id = ExtraerIdDePlanilla(r.Planilla);

        // Lo normal: la cuenta de servicio ya la puso quien instaló el servidor y el cliente solo comparte su planilla.
        // Si el servidor no tiene ninguna, hay que cargar la credencial desde la app.
        var cargadaDesdeLaApp = !string.IsNullOrWhiteSpace(r.Credenciales);
        if (!cargadaDesdeLaApp && !_cliente.TieneCredenciales)
            throw new ConexionInvalidaException("Este servidor no tiene una cuenta de servicio de Google. Cargá el archivo .json o pedíselo a quien instaló el servidor.");

        var credenciales = cargadaDesdeLaApp ? r.Credenciales!.Trim() : _cliente.Credenciales!;
        var correo = ValidarCredenciales(credenciales);

        NuevoAdministrador? admin = null;
        if (!string.IsNullOrWhiteSpace(r.EmailAdministrador) || !string.IsNullOrWhiteSpace(r.PinAdministrador)
            || !string.IsNullOrWhiteSpace(r.NombreAdministrador))
        {
            if (string.IsNullOrWhiteSpace(r.NombreAdministrador)
                || string.IsNullOrWhiteSpace(r.EmailAdministrador) || !r.EmailAdministrador.Contains('@'))
                throw new ConexionInvalidaException("Completá el nombre y un email válido del administrador.");
            if (!PinValido.IsMatch(r.PinAdministrador ?? ""))
                throw new ConexionInvalidaException("El PIN del administrador debe tener 4 dígitos.");

            admin = new NuevoAdministrador(r.NombreAdministrador.Trim(), r.EmailAdministrador.Trim(), r.PinAdministrador!);
        }

        await _configuracion.WaitAsync();
        try
        {
            await _probador.ProbarAsync(id, credenciales);

            _almacen.Guardar(id, cargadaDesdeLaApp ? credenciales : null);
            _cliente.Conectar(id, credenciales);
            await _inicializador.InicializarAsync(admin);
            return correo;
        }
        finally { _configuracion.Release(); }
    }
}
