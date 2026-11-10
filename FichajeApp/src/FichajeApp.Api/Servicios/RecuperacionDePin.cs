using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using Microsoft.Extensions.Options;

namespace FichajeApp.Api.Servicios;

// "Olvidé mi PIN": se manda un código de 6 dígitos al mail de la persona y, con el código, elige un PIN nuevo.
// Los códigos viven en memoria (una sola instancia de la API; si se reinicia, hay que pedir otro). Se guardan hasheados.
public class RecuperacionDePin
{
    public const int MinutosDeVigencia = 15;
    public const int MaxIntentos = 5;
    private static readonly TimeSpan EntreSolicitudes = TimeSpan.FromSeconds(60);
    private const string CodigoInvalido = "El código no es válido o venció. Pedí uno nuevo.";

    private sealed class Pedido
    {
        public string Hash = "";
        public DateTime Vence;
        public DateTime Solicitado;
        public int Intentos;
    }

    private readonly ConcurrentDictionary<string, Pedido> _pedidos = new();
    private readonly IEmpleadosRepositorio _empleados;
    private readonly IEnviadorDeCorreo _correo;
    private readonly NotificacionesService _notificaciones;
    private readonly BitacoraService _bitacora;
    private readonly HistorialUsuariosService _historial;
    private readonly TimeProvider _tiempo;
    private readonly string _app;
    private readonly SesionesVigentes? _sesiones;
    private readonly LoginService? _login;

    public RecuperacionDePin(IEmpleadosRepositorio empleados, IEnviadorDeCorreo correo, NotificacionesService notificaciones,
        BitacoraService bitacora, HistorialUsuariosService historial, TimeProvider tiempo, IOptions<OpcionesCorreo> opciones,
        SesionesVigentes? sesiones = null, LoginService? login = null)
    {
        _empleados = empleados;
        _correo = correo;
        _notificaciones = notificaciones;
        _bitacora = bitacora;
        _historial = historial;
        _tiempo = tiempo;
        _app = opciones.Value.NombreApp;
        _sesiones = sesiones;
        _login = login;
    }

    public bool CorreoDisponible => _correo.Configurado;

    private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

    private static string Clave(string email) => email.Trim().ToLowerInvariant();

    private static string Hash(string email, string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Clave(email) + "|" + codigo)));

    // No dice si el mail existe: la respuesta es la misma para cualquiera
    public async Task SolicitarAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !CorreoDisponible) return;
        var clave = Clave(email);

        var empleado = await _empleados.BuscarPorEmailAsync(clave);
        if (empleado is null) return;

        if (_pedidos.TryGetValue(clave, out var previo) && Ahora - previo.Solicitado < EntreSolicitudes) return;

        var codigo = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        _pedidos[clave] = new Pedido { Hash = Hash(clave, codigo), Vence = Ahora.AddMinutes(MinutosDeVigencia), Solicitado = Ahora };

        var (asunto, html, texto) = PlantillasCorreo.CodigoDeRecuperacion(_app, empleado.NombreCompleto, codigo, MinutosDeVigencia);
        await _correo.EnviarAsync(empleado.Email, asunto, html, texto);
        await _bitacora.RegistrarAsync(empleado.Email, "PIN_CODIGO_SOLICITADO");
    }

    public async Task RestablecerAsync(string? email, string? codigo, string? pinNuevo)
    {
        var errorPin = ValidadorPin.ObtenerError(pinNuevo ?? "");
        if (errorPin is not null) throw new InvalidOperationException(errorPin);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(codigo)) throw new InvalidOperationException(CodigoInvalido);

        var clave = Clave(email);
        if (!_pedidos.TryGetValue(clave, out var pedido) || pedido.Vence < Ahora)
        {
            _pedidos.TryRemove(clave, out _);
            throw new InvalidOperationException(CodigoInvalido);
        }

        if (++pedido.Intentos > MaxIntentos)
        {
            _pedidos.TryRemove(clave, out _);
            throw new InvalidOperationException(CodigoInvalido);
        }

        var esperado = Encoding.UTF8.GetBytes(pedido.Hash);
        var recibido = Encoding.UTF8.GetBytes(Hash(clave, codigo.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(esperado, recibido))
            throw new InvalidOperationException(CodigoInvalido);

        var empleado = await _empleados.BuscarPorEmailAsync(clave)
            ?? throw new InvalidOperationException(CodigoInvalido);

        empleado.PinHash = BCrypt.Net.BCrypt.HashPassword(pinNuevo);
        empleado.Bloqueado = false;
        empleado.IntentosFallidos = 0;
        await _empleados.ActualizarAsync(empleado);
        _sesiones?.Invalidar(empleado.Id);
        _login?.Liberar(empleado.Id);   // el bloqueo temporal por intentos fallidos vive en memoria
        _pedidos.TryRemove(clave, out _);

        await _historial.RegistrarAsync(empleado, "CAMBIO_PIN", empleado.Email);
        await _bitacora.RegistrarAsync(empleado.Email, "PIN_RESTABLECIDO_POR_CODIGO");
        await _notificaciones.CrearAsync(empleado.Id, "Cambiaste tu PIN",
            "Tu PIN se cambió con un código enviado a tu mail. Si no fuiste vos, avisale a tu administrador.");
    }
}
