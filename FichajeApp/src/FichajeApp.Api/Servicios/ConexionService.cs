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

    // "Todo de una vez": el servidor crea la planilla nueva, la prepara y se la comparte a EmailPropietario
    public bool CrearPlanillaNueva { get; set; }
    public string? EmailPropietario { get; set; }
    public string? NombreEmpresa { get; set; }
}

public class ConexionInvalidaException : Exception
{
    public ConexionInvalidaException(string mensaje) : base(mensaje) { }
}

public record ResultadoConfiguracion(string CuentaDeServicio, string? PlanillaCreadaUrl = null, string? PlanillaCreadaTitulo = null,
    string? CompartidaCon = null);

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
            throw new ConexionInvalidaException("No se encontró la planilla. Revisá el link o el ID (puede que se haya borrado).");
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

// Configuración de la conexión con Google Sheets desde la app: el cliente carga su planilla (o pide que se cree una nueva)
// y se guarda cifrada en el servidor.
public class ConexionService
{
    private static readonly Regex IdEnLink = new(@"/spreadsheets/d/([a-zA-Z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex IdSuelto = new(@"^[a-zA-Z0-9_-]{20,}$", RegexOptions.Compiled);
    private static readonly Regex PinValido = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly SemaphoreSlim _configuracion = new(1, 1);

    // Cada cuánto se vuelve a preguntar a Google si la planilla conectada sigue existiendo y compartida
    private static readonly TimeSpan VidaDelControl = TimeSpan.FromSeconds(20);

    private readonly GoogleSheetsClient _cliente;
    private readonly AlmacenConexion _almacen;
    private readonly IProbadorConexion _probador;
    private readonly IInicializadorPlanilla _inicializador;
    private readonly ICreadorDePlanilla _creador;
    private readonly TimeProvider _tiempo;
    private readonly ILogger<ConexionService> _log;

    private readonly object _candado = new();
    private DateTime _controladoEn = DateTime.MinValue;
    private bool _accesible = true;
    private string? _aviso;

    public ConexionService(GoogleSheetsClient cliente, AlmacenConexion almacen, IProbadorConexion probador,
        IInicializadorPlanilla inicializador, ICreadorDePlanilla creador, ILogger<ConexionService> log, TimeProvider? tiempo = null)
    {
        _cliente = cliente;
        _almacen = almacen;
        _probador = probador;
        _inicializador = inicializador;
        _creador = creador;
        _tiempo = tiempo ?? TimeProvider.System;
        _log = log;
    }

    // Hay una planilla conectada (puede ser que ya no exista: ver EstadoAsync)
    public bool Configurada => _cliente.Configurado;

    // Correo con el que hay que compartir la planilla; null si el servidor no tiene credencial (se carga desde la app)
    public string? CuentaDeServicio => _cliente.CorreoCuentaServicio;

    public record EstadoDeConexion(bool Conectada, bool Accesible, string? Aviso)
    {
        // Lo que la app necesita saber: ¿se puede trabajar? Una planilla borrada o des-compartida cuenta como "sin configurar"
        public bool Lista => Conectada && Accesible;
    }

    // Controla (como mucho cada 20 s) que la planilla conectada siga existiendo y compartida con la cuenta de servicio.
    // Si Google no responde por un problema de red no se puede saber: se asume que está bien para no dejar a nadie afuera.
    public async Task<EstadoDeConexion> EstadoAsync()
    {
        if (!_cliente.Configurado) return new EstadoDeConexion(false, false, null);

        lock (_candado)
        {
            if (_tiempo.GetUtcNow().UtcDateTime - _controladoEn < VidaDelControl)
                return new EstadoDeConexion(true, _accesible, _aviso);
        }

        bool accesible = true;
        string? aviso = null;
        try
        {
            await _probador.ProbarAsync(_cliente.SpreadsheetId, _cliente.Credenciales!);
        }
        catch (ConexionInvalidaException ex)
        {
            accesible = false;
            aviso = "La planilla conectada ya no está disponible. " + ex.Message;
            _log.LogWarning("La planilla conectada no es accesible: {Motivo}", ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo comprobar la planilla conectada (se asume que está bien)");
        }

        lock (_candado)
        {
            _controladoEn = _tiempo.GetUtcNow().UtcDateTime;
            _accesible = accesible;
            _aviso = aviso;
        }
        return new EstadoDeConexion(true, accesible, aviso);
    }

    private void OlvidarElControl()
    {
        lock (_candado) { _controladoEn = DateTime.MinValue; _accesible = true; _aviso = null; }
    }

    public enum Acceso { Permitido, ClaveIncorrecta, SoloAdministrador }

    // Sin planilla (o con una que ya no existe) alcanza el código de activación: si no, nadie podría volver a conectar,
    // porque los administradores vivían en la planilla perdida. Con una planilla en uso, solo un administrador con sesión.
    public async Task<Acceso> VerificarAccesoAsync(bool esAdministrador, string? clave)
    {
        var estado = await EstadoAsync();
        if (estado.Lista) return esAdministrador ? Acceso.Permitido : Acceso.SoloAdministrador;
        return ClaveCorrecta(clave) ? Acceso.Permitido : Acceso.ClaveIncorrecta;
    }

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

    // Mensaje corto y entendible de un error técnico (el detalle completo queda en el log del servidor)
    private static string Resumir(Exception ex) => ex switch
    {
        GoogleApiException g when (int)g.HttpStatusCode == 429 => "Google limitó los pedidos por minuto.",
        GoogleApiException g => $"Google respondió con un error ({(int)g.HttpStatusCode}).",
        _ => ex.Message
    };

    private static string TituloDeLaPlanilla(ConfigurarConexionRequest r)
    {
        var nombre = new[] { r.NombreEmpresa, r.NombreAdministrador }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim();
        return string.IsNullOrEmpty(nombre) ? "Fichaje" : $"Fichaje - {nombre}";
    }

    public async Task<ResultadoConfiguracion> ConfigurarAsync(ConfigurarConexionRequest r)
    {
        // Lo normal: la cuenta de servicio ya la puso quien instaló el servidor y el cliente solo comparte su planilla.
        // Si el servidor no tiene ninguna, hay que cargar la credencial desde la app.
        var cargadaDesdeLaApp = !string.IsNullOrWhiteSpace(r.Credenciales);
        if (!cargadaDesdeLaApp && !_cliente.TieneCredenciales)
            throw new ConexionInvalidaException("Este servidor no tiene una cuenta de servicio de Google. Cargá el archivo .json o pedíselo a quien instaló el servidor.");

        var credenciales = cargadaDesdeLaApp ? r.Credenciales!.Trim() : _cliente.Credenciales!;
        var correo = ValidarCredenciales(credenciales);

        // Con planilla nueva no hay link: se valida el email al que se le comparte
        string? emailPropietario = null;
        string? id = null;
        if (r.CrearPlanillaNueva)
        {
            emailPropietario = (r.EmailPropietario ?? "").Trim();
            if (!emailPropietario.Contains('@') || emailPropietario.Contains(' '))
                throw new ConexionInvalidaException("Escribí tu email de Google: ahí se te comparte la planilla nueva.");
        }
        else
        {
            id = ExtraerIdDePlanilla(r.Planilla);
        }

        // El administrador es obligatorio si la planilla es nueva (sin él nadie podría entrar); si no, solo si se completa algo
        NuevoAdministrador? admin = null;
        bool pideAdmin = r.CrearPlanillaNueva || !string.IsNullOrWhiteSpace(r.EmailAdministrador)
            || !string.IsNullOrWhiteSpace(r.PinAdministrador) || !string.IsNullOrWhiteSpace(r.NombreAdministrador);
        if (pideAdmin)
        {
            var emailAdmin = string.IsNullOrWhiteSpace(r.EmailAdministrador) ? emailPropietario : r.EmailAdministrador.Trim();
            if (string.IsNullOrWhiteSpace(r.NombreAdministrador) || string.IsNullOrWhiteSpace(emailAdmin) || !emailAdmin.Contains('@'))
                throw new ConexionInvalidaException("Completá el nombre y un email válido del administrador.");
            if (!PinValido.IsMatch(r.PinAdministrador ?? ""))
                throw new ConexionInvalidaException("El PIN del administrador debe tener 4 dígitos.");

            admin = new NuevoAdministrador(r.NombreAdministrador.Trim(), emailAdmin, r.PinAdministrador!);
        }

        await _configuracion.WaitAsync();
        string? creadaAca = null; // para borrarla si algo falla después
        try
        {
            string? titulo = null;
            if (r.CrearPlanillaNueva)
            {
                titulo = TituloDeLaPlanilla(r);
                id = creadaAca = await _creador.CrearAsync(credenciales, titulo, emailPropietario!);
            }
            else
            {
                await _probador.ProbarAsync(id!, credenciales);
            }

            // Lo que había antes, para volver a eso si la preparación de la planilla nueva falla a la mitad
            var anteriorGuardado = _almacen.Leer();
            var anteriorId = _cliente.Configurado ? _cliente.SpreadsheetId : null;
            var anteriorCredenciales = _cliente.Credenciales;

            _almacen.Guardar(id!, cargadaDesdeLaApp ? credenciales : null);
            _cliente.Conectar(id!, credenciales);
            try
            {
                await _inicializador.InicializarAsync(admin);
            }
            catch (Exception ex)
            {
                // Si quedara conectada pero a medio preparar, no se podría reintentar (el código ya no alcanza): se deshace todo
                _log.LogError(ex, "No se pudo preparar la planilla; se vuelve a la conexión anterior");
                if (anteriorGuardado is not null) _almacen.Guardar(anteriorGuardado.Value.SpreadsheetId, anteriorGuardado.Value.CredencialesJson);
                else _almacen.Borrar();

                if (anteriorId is not null) _cliente.Conectar(anteriorId, anteriorCredenciales);
                else _cliente.Desconectar();
                OlvidarElControl();

                if (creadaAca is not null)
                {
                    try { await _creador.EliminarAsync(credenciales, creadaAca); }
                    catch (Exception e) { _log.LogWarning(e, "No se pudo borrar la planilla recién creada {Id}", creadaAca); }
                }

                throw new ConexionInvalidaException(
                    "Se pudo entrar a la planilla pero no se pudo prepararla: " + Resumir(ex) + " No se guardó nada: probá de nuevo en un minuto.");
            }

            OlvidarElControl();
            return r.CrearPlanillaNueva
                ? new ResultadoConfiguracion(correo, $"https://docs.google.com/spreadsheets/d/{id}", titulo, emailPropietario)
                : new ResultadoConfiguracion(correo);
        }
        finally { _configuracion.Release(); }
    }
}
