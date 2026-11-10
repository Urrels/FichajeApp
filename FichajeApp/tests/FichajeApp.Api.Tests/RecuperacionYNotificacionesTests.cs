using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class CorreoFalso : IEnviadorDeCorreo
{
    public bool Configurado { get; set; } = true;
    public List<(string Para, string Asunto, string Texto)> Enviados { get; } = new();

    public Task<bool> EnviarAsync(string para, string asunto, string html, string texto)
    {
        Enviados.Add((para, asunto, texto));
        return Task.FromResult(true);
    }

    // El código de 6 dígitos del último mail
    public string UltimoCodigo() => System.Text.RegularExpressions.Regex.Match(Enviados.Last().Texto, @"\b\d{6}\b").Value;
}

public class NotificacionesRepoFalso : INotificacionesRepositorio
{
    public List<Notificacion> Datos { get; } = new();
    public bool Falla { get; set; }

    public Task AgregarAsync(Notificacion n)
    {
        if (Falla) throw new InvalidOperationException("base caída");
        n.Id = Datos.Count + 1;
        Datos.Add(n);
        return Task.CompletedTask;
    }

    public Task<List<Notificacion>> ListarAsync(int empleadoId, int maximo) =>
        Task.FromResult(Datos.Where(d => d.EmpleadoId == empleadoId).OrderByDescending(d => d.Fecha).Take(maximo).ToList());

    public Task<int> ContarNoLeidasAsync(int empleadoId) => Task.FromResult(Datos.Count(d => d.EmpleadoId == empleadoId && !d.Leida));

    public Task MarcarLeidasAsync(int empleadoId, int? id)
    {
        foreach (var d in Datos.Where(d => d.EmpleadoId == empleadoId && (id == null || d.Id == id))) d.Leida = true;
        return Task.CompletedTask;
    }
}

public class RecuperacionYNotificacionesTests
{
    private readonly Entorno _e = new();
    private readonly CorreoFalso _correo = new();
    private readonly NotificacionesRepoFalso _repo = new();
    private readonly NotificacionesService _notificaciones;
    private readonly RecuperacionDePin _recuperacion;

    public RecuperacionYNotificacionesTests()
    {
        _notificaciones = new NotificacionesService(_repo, _e.Reloj);
        _recuperacion = new RecuperacionDePin(_e.Empleados, _correo, _notificaciones, _e.Bitacora, _e.Historial, _e.Reloj,
            Microsoft.Extensions.Options.Options.Create(new OpcionesCorreo()));
    }

    // ---- Recuperar el PIN ----

    [Fact]
    public async Task SolicitarCodigo_MandaUnMailConUnCodigoDeSeisDigitos()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");

        var mail = Assert.Single(_correo.Enviados);
        Assert.Equal("ana@test.com", mail.Para);
        Assert.Matches(@"^\d{6}$", _correo.UltimoCodigo());
    }

    [Fact]
    public async Task SolicitarCodigo_DeUnMailQueNoExisteNoManaNadaNiFalla()
    {
        await _recuperacion.SolicitarAsync("nadie@test.com");
        Assert.Empty(_correo.Enviados);
    }

    [Fact]
    public async Task SolicitarCodigo_SinMailConfigurado_NoManaNada()
    {
        _correo.Configurado = false;
        await _recuperacion.SolicitarAsync("ana@test.com");
        Assert.Empty(_correo.Enviados);
        Assert.False(_recuperacion.CorreoDisponible);
    }

    [Fact]
    public async Task SolicitarCodigo_DosVecesSeguidasManaUnSoloMail()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        await _recuperacion.SolicitarAsync("ana@test.com");
        Assert.Single(_correo.Enviados);

        _e.Reloj.Ahora = _e.Reloj.Ahora.AddSeconds(61);
        await _recuperacion.SolicitarAsync("ana@test.com");
        Assert.Equal(2, _correo.Enviados.Count);
    }

    [Fact]
    public async Task ConElCodigoCorrecto_SeCambiaElPinYSeDesbloquea()
    {
        var ana = _e.Empleados.Datos.Single(x => x.Email == "ana@test.com");
        ana.Bloqueado = true;
        ana.IntentosFallidos = 3;
        await _recuperacion.SolicitarAsync("ana@test.com");

        await _recuperacion.RestablecerAsync("ANA@test.com", _correo.UltimoCodigo(), "5827");

        ana = _e.Empleados.Datos.Single(x => x.Email == "ana@test.com");   // el repositorio guarda una copia nueva
        Assert.True(BCrypt.Net.BCrypt.Verify("5827", ana.PinHash));
        Assert.False(ana.Bloqueado);
        Assert.Equal(0, ana.IntentosFallidos);
        Assert.Contains(_repo.Datos, n => n.EmpleadoId == ana.Id && n.Titulo.Contains("PIN"));
    }

    [Fact]
    public async Task ElCodigoSirveUnaSolaVez()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        var codigo = _correo.UltimoCodigo();
        await _recuperacion.RestablecerAsync("ana@test.com", codigo, "5827");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", codigo, "6139"));
    }

    [Fact]
    public async Task UnCodigoIncorrecto_NoCambiaElPin()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        var anterior = _e.Empleados.Datos.Single(x => x.Email == "ana@test.com").PinHash;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", "000000", "5827"));

        Assert.Equal(anterior, _e.Empleados.Datos.Single(x => x.Email == "ana@test.com").PinHash);
    }

    [Fact]
    public async Task DespuesDeCincoIntentosFallidos_ElCodigoSeInvalida()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        var correcto = _correo.UltimoCodigo();
        var equivocado = correcto == "111111" ? "222222" : "111111";

        for (int i = 0; i < RecuperacionDePin.MaxIntentos; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", equivocado, "5827"));

        // Ni siquiera el código correcto sirve ya: hay que pedir otro
        await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", correcto, "5827"));
    }

    [Fact]
    public async Task ElCodigoVenceALosQuinceMinutos()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        var codigo = _correo.UltimoCodigo();

        _e.Reloj.Ahora = _e.Reloj.Ahora.AddMinutes(RecuperacionDePin.MinutosDeVigencia + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", codigo, "5827"));
    }

    [Fact]
    public async Task ElPinNuevoTieneQueSerSeguro()
    {
        await _recuperacion.SolicitarAsync("ana@test.com");
        var codigo = _correo.UltimoCodigo();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _recuperacion.RestablecerAsync("ana@test.com", codigo, "1234"));
        // El intento rechazado por el PIN no gasta el código
        await _recuperacion.RestablecerAsync("ana@test.com", codigo, "5827");
    }

    // ---- Notificaciones ----

    [Fact]
    public async Task LasNotificacionesSonDeCadaPersona_YSeMarcanComoLeidas()
    {
        await _notificaciones.CrearAsync(2, "Hola Ana", "Mensaje");
        await _notificaciones.CrearAsync(3, "Hola Beto", "Mensaje");

        Assert.Equal(1, await _notificaciones.ContarNoLeidasAsync(2));
        Assert.Single(await _notificaciones.ListarAsync(2));

        await _notificaciones.MarcarLeidasAsync(2);

        Assert.Equal(0, await _notificaciones.ContarNoLeidasAsync(2));
        Assert.Equal(1, await _notificaciones.ContarNoLeidasAsync(3));
    }

    [Fact]
    public async Task UnaNotificacionQueFalla_NoRompeLaOperacion()
    {
        _repo.Falla = true;
        await _notificaciones.CrearAsync(2, "Hola", "Mensaje");   // no lanza
    }

    [Fact]
    public async Task ElAltaDeUsuario_ManaBienvenidaPorMailYPorAviso_SinIncluirElPin()
    {
        var usuarios = new UsuariosAdminService(_e.Empleados, _e.Roles, _e.Fichajes, _e.Historial, _e.HistorialRepo, _e.Guarda,
            _e.Bitacora, _e.Permisos, sesiones: null, correo: _correo, notificaciones: _notificaciones,
            opcionesCorreo: Microsoft.Extensions.Options.Options.Create(new OpcionesCorreo()));
        var rolEmpleado = (await _e.Roles.ListarRolesAsync()).Single(r => r.Nombre == "Empleado").Id;

        var id = await usuarios.CrearAsync("admin@test.com", "Carla Nueva", "carla@test.com", "5827", new List<int> { rolEmpleado });

        var mail = Assert.Single(_correo.Enviados);
        Assert.Equal("carla@test.com", mail.Para);
        Assert.Contains("Carla Nueva", mail.Texto);
        Assert.DoesNotContain("5827", mail.Texto);
        Assert.Contains(_repo.Datos, n => n.EmpleadoId == id && n.Titulo.Contains("Bienvenido"));
    }

    [Fact]
    public async Task ElMailDeBienvenidaEscapaElNombre()
    {
        var (_, html, _) = PlantillasCorreo.Bienvenida("HoraTrack", "<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html);
    }
}
