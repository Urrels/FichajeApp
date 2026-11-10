using FichajeApp.Api.Services;

namespace FichajeApp.Api.Servicios;

// Si el teléfono se apagó o la app se cerró con la jornada abierta, el registro del recorrido se corta y Android no lo vuelve a
// encender solo. Este aviso (un push) le pide a la persona que abra la app para retomarlo. Solo si la empresa activó el recorrido.
public class RecordatorioRecorrido
{
    // Cuánto sin puntos nuevos se considera "cortado" y cada cuánto se le recuerda a la misma persona como máximo
    public static readonly TimeSpan SinPuntos = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan EntreAvisos = TimeSpan.FromHours(3);

    // Estático: el servicio vive un ciclo (es scoped) pero a quién se avisó hace poco tiene que recordarse entre ciclos
    private static readonly Dictionary<string, DateTime> _ultimoAviso = new();
    private readonly ConsultaFichajesService _consulta;
    private readonly Repositorios.IEmpleadosRepositorio _empleados;
    private readonly IRecorridoRepositorio _recorrido;
    private readonly PushService _push;
    private readonly IFichajeService _fichaje;
    private readonly TimeProvider _tiempo;

    public RecordatorioRecorrido(ConsultaFichajesService consulta, Repositorios.IEmpleadosRepositorio empleados, IRecorridoRepositorio recorrido,
        PushService push, IFichajeService fichaje, TimeProvider tiempo)
    {
        _consulta = consulta;
        _empleados = empleados;
        _recorrido = recorrido;
        _push = push;
        _fichaje = fichaje;
        _tiempo = tiempo;
    }

    // Devuelve a cuántas personas se les avisó
    public async Task<int> EjecutarAsync()
    {
        if (!_push.Configurado) return 0;
        if (!(await _fichaje.ConfiguracionAsync()).SeguimientoRecorrido) return 0;

        var ahora = _tiempo.GetLocalNow().DateTime;
        var personas = await _empleados.ListarAsync();
        int avisados = 0;

        foreach (var entrada in await _consulta.ObtenerPresentesAsync())
        {
            // Entró hace poco: todavía no hay por qué esperar puntos
            if (ahora - entrada.FechaHora < SinPuntos) continue;
            if (_ultimoAviso.TryGetValue(entrada.NombreCompleto, out var antes) && ahora - antes < EntreAvisos) continue;

            var persona = personas.FirstOrDefault(p => p.Activo && p.NombreCompleto.Equals(entrada.NombreCompleto, StringComparison.OrdinalIgnoreCase));
            if (persona is null) continue;

            var recientes = await _recorrido.ListarAsync(persona.Id, ahora - SinPuntos, ahora.AddMinutes(5));
            if (recientes.Count > 0) continue;

            _ultimoAviso[entrada.NombreCompleto] = ahora;
            await _push.NotificarAsync(persona.Id, "Retomá el registro de tu recorrido",
                "Tu jornada sigue abierta pero el registro se cortó. Abrí la app para retomarlo.");
            avisados++;
        }
        return avisados;
    }
}

// Cada 10 minutos
public class RecordatorioRecorridoHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _alcances;
    private readonly ILogger<RecordatorioRecorridoHostedService> _log;

    public RecordatorioRecorridoHostedService(IServiceScopeFactory alcances, ILogger<RecordatorioRecorridoHostedService> log)
    {
        _alcances = alcances;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await reloj.WaitForNextTickAsync(cancelar))
        {
            try { using var alcance = _alcances.CreateScope(); await alcance.ServiceProvider.GetRequiredService<RecordatorioRecorrido>().EjecutarAsync(); }
            catch (Exception ex) { _log.LogWarning("No se pudo revisar el recorrido: {Tipo}", ex.GetType().Name); }
        }
    }
}
