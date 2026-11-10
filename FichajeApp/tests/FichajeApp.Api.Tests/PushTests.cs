using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class DispositivosFalso : IDispositivosPushRepositorio
{
    public Dictionary<string, int> Tokens { get; } = new();

    public Task GuardarAsync(int empleadoId, string token, DateTime ahora)
    {
        Tokens[token] = empleadoId;   // un token pertenece a una sola persona
        return Task.CompletedTask;
    }

    public Task EliminarAsync(string token)
    {
        Tokens.Remove(token);
        return Task.CompletedTask;
    }

    public Task<List<string>> ListarTokensAsync(int empleadoId) =>
        Task.FromResult(Tokens.Where(t => t.Value == empleadoId).Select(t => t.Key).ToList());
}

public class EnviadorPushFalso : IEnviadorPush
{
    public bool Configurado { get; set; } = true;
    public bool Falla { get; set; }
    public List<string> Invalidos { get; } = new();
    public List<(string Token, string Titulo, string Mensaje)> Enviados { get; } = new();

    public Task<List<string>> EnviarAsync(IReadOnlyCollection<string> tokens, string titulo, string mensaje)
    {
        if (Falla) throw new InvalidOperationException("Firebase caído");
        Enviados.AddRange(tokens.Select(t => (t, titulo, mensaje)));
        return Task.FromResult(Invalidos.ToList());
    }
}

public class PushTests
{
    private readonly Entorno _e = new();
    private readonly DispositivosFalso _dispositivos = new();
    private readonly EnviadorPushFalso _enviador = new();
    private readonly NotificacionesRepoFalso _notificacionesRepo = new();
    private readonly PushService _push;
    private readonly NotificacionesService _notificaciones;

    public PushTests()
    {
        _push = new PushService(_dispositivos, _enviador, _e.Reloj);
        _notificaciones = new NotificacionesService(_notificacionesRepo, _e.Reloj, push: _push);
    }

    [Fact]
    public async Task UnAvisoLlegaAlTelefonoDeLaPersona_YSoloAEse()
    {
        await _push.RegistrarAsync(2, "token-ana");
        await _push.RegistrarAsync(3, "token-beto");

        await _notificaciones.CrearAsync(2, "Tu horario cambió", "Revisalo");

        var enviado = Assert.Single(_enviador.Enviados);
        Assert.Equal("token-ana", enviado.Token);
        Assert.Equal("Tu horario cambió", enviado.Titulo);
        Assert.Single(_notificacionesRepo.Datos);   // y además queda en el centro de notificaciones
    }

    [Fact]
    public async Task UnTelefonoQueCambiaDeCuenta_RecibeSoloLosAvisosDeLaNueva()
    {
        await _push.RegistrarAsync(2, "token-compartido");
        await _push.RegistrarAsync(3, "token-compartido");

        await _push.NotificarAsync(2, "Hola", "Ana");
        Assert.Empty(_enviador.Enviados);

        await _push.NotificarAsync(3, "Hola", "Beto");
        Assert.Single(_enviador.Enviados);
    }

    [Fact]
    public async Task AlCerrarSesion_ElTelefonoDejaDeRecibir()
    {
        await _push.RegistrarAsync(2, "token-ana");
        await _push.QuitarAsync("token-ana");

        await _push.NotificarAsync(2, "Hola", "Ana");

        Assert.Empty(_enviador.Enviados);
    }

    [Fact]
    public async Task LosTokensQueFirebaseDiceQueYaNoExisten_SeBorran()
    {
        await _push.RegistrarAsync(2, "token-viejo");
        _enviador.Invalidos.Add("token-viejo");

        await _push.NotificarAsync(2, "Hola", "Ana");

        Assert.Empty(_dispositivos.Tokens);
    }

    [Fact]
    public async Task SinFirebaseConfigurado_NoSeMandaNadaYElAvisoIgualQueda()
    {
        _enviador.Configurado = false;
        await _push.RegistrarAsync(2, "token-ana");

        await _notificaciones.CrearAsync(2, "Hola", "Ana");

        Assert.Empty(_enviador.Enviados);
        Assert.Single(_notificacionesRepo.Datos);
    }

    [Fact]
    public async Task SiFirebaseFalla_NoSeRompeLaOperacionQueOriginaElAviso()
    {
        _enviador.Falla = true;
        await _push.RegistrarAsync(2, "token-ana");

        await _notificaciones.CrearAsync(2, "Hola", "Ana");   // no lanza

        Assert.Single(_notificacionesRepo.Datos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnTokenVacioSeRechaza(string token)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _push.RegistrarAsync(2, token));
    }

    [Fact]
    public async Task UnTokenGigante_SeRechaza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _push.RegistrarAsync(2, new string('x', 401)));
    }
}
