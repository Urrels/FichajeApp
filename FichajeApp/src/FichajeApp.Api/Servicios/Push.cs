using FichajeApp.Api.Persistencia;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FichajeApp.Api.Servicios;

// Notificaciones push con Firebase Cloud Messaging. Se configura con la clave de la cuenta de servicio de Firebase
// (el contenido del JSON) en la variable de entorno Push__CredencialesJson. Sin ella no se manda ningún push y la app funciona igual.
public class OpcionesPush
{
    public const string Seccion = "Push";

    public string CredencialesJson { get; set; } = "";

    public bool Configurado => !string.IsNullOrWhiteSpace(CredencialesJson);
}

public interface IEnviadorPush
{
    bool Configurado { get; }
    // Devuelve los tokens que Firebase dijo que ya no existen (app desinstalada), para borrarlos
    Task<List<string>> EnviarAsync(IReadOnlyCollection<string> tokens, string titulo, string mensaje);
}

public class FirebaseEnviador : IEnviadorPush
{
    private readonly FirebaseMessaging _mensajeria;

    public FirebaseEnviador(IOptions<OpcionesPush> opciones)
    {
        var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(opciones.Value.CredencialesJson)
        });
        _mensajeria = FirebaseMessaging.GetMessaging(app);
    }

    public bool Configurado => true;

    public async Task<List<string>> EnviarAsync(IReadOnlyCollection<string> tokens, string titulo, string mensaje)
    {
        var invalidos = new List<string>();
        if (tokens.Count == 0) return invalidos;

        var mensajes = tokens.Select(t => new Message
        {
            Token = t,
            Notification = new Notification { Title = titulo, Body = mensaje },
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification { ChannelId = "avisos" }
            }
        }).ToList();

        var respuesta = await _mensajeria.SendEachAsync(mensajes);
        for (int i = 0; i < respuesta.Responses.Count; i++)
        {
            var r = respuesta.Responses[i];
            if (!r.IsSuccess && r.Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                invalidos.Add(mensajes[i].Token);
        }
        return invalidos;
    }
}

public class EnviadorPushNulo : IEnviadorPush
{
    public bool Configurado => false;
    public Task<List<string>> EnviarAsync(IReadOnlyCollection<string> tokens, string titulo, string mensaje) => Task.FromResult(new List<string>());
}

public interface IDispositivosPushRepositorio
{
    // Un token pertenece a un solo empleado: si el teléfono cambia de cuenta, pasa a la nueva
    Task GuardarAsync(int empleadoId, string token, DateTime ahora);
    Task EliminarAsync(string token);
    Task<List<string>> ListarTokensAsync(int empleadoId);
}

public class DispositivosPushSqlRepositorio : Persistencia.Sql.BaseSql, IDispositivosPushRepositorio
{
    public DispositivosPushSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task GuardarAsync(int empleadoId, string token, DateTime ahora)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = await db.DispositivosPush.FirstOrDefaultAsync(d => d.Token == token);
        if (fila is null) db.DispositivosPush.Add(fila = new DispositivoPushFila { Token = token });
        fila.EmpleadoId = empleadoId;
        fila.Actualizado = ahora;
        await db.SaveChangesAsync();
    }

    public async Task EliminarAsync(string token)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        await db.DispositivosPush.Where(d => d.Token == token).ExecuteDeleteAsync();
    }

    public Task<List<string>> ListarTokensAsync(int empleadoId) =>
        LeerAsync(db => db.DispositivosPush.Where(d => d.EmpleadoId == empleadoId).Select(d => d.Token).ToListAsync());
}

public class DispositivosPushNoDisponible : IDispositivosPushRepositorio
{
    public Task GuardarAsync(int empleadoId, string token, DateTime ahora) => Task.CompletedTask;
    public Task EliminarAsync(string token) => Task.CompletedTask;
    public Task<List<string>> ListarTokensAsync(int empleadoId) => Task.FromResult(new List<string>());
}

// Registra los teléfonos de cada persona y les manda los avisos. Mandar un push nunca rompe la operación que lo origina.
public class PushService
{
    private const int LargoMaximoToken = 400;

    private readonly IDispositivosPushRepositorio _repo;
    private readonly IEnviadorPush _enviador;
    private readonly TimeProvider _tiempo;
    private readonly ILogger<PushService>? _log;

    public PushService(IDispositivosPushRepositorio repo, IEnviadorPush enviador, TimeProvider tiempo, ILogger<PushService>? log = null)
    {
        _repo = repo;
        _enviador = enviador;
        _tiempo = tiempo;
        _log = log;
    }

    public bool Configurado => _enviador.Configurado;

    public async Task RegistrarAsync(int empleadoId, string? token)
    {
        token = (token ?? "").Trim();
        if (token.Length == 0 || token.Length > LargoMaximoToken)
            throw new InvalidOperationException("El código del dispositivo no es válido.");
        await _repo.GuardarAsync(empleadoId, token, _tiempo.GetUtcNow().UtcDateTime);
    }

    public Task QuitarAsync(string? token) =>
        string.IsNullOrWhiteSpace(token) ? Task.CompletedTask : _repo.EliminarAsync(token.Trim());

    public async Task NotificarAsync(int empleadoId, string titulo, string mensaje)
    {
        if (!_enviador.Configurado) return;
        try
        {
            var tokens = await _repo.ListarTokensAsync(empleadoId);
            var invalidos = await _enviador.EnviarAsync(tokens, titulo, mensaje);
            foreach (var t in invalidos) await _repo.EliminarAsync(t);
        }
        catch (Exception ex) { _log?.LogWarning("No se pudo mandar un push: {Tipo}", ex.GetType().Name); }
    }
}
