using System.Collections.Concurrent;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Avisos que no salen de un fichaje en sí: una huella o una cara que no pasó, o una jornada que quedó abierta demasiado
// tiempo. Van por la jerarquía (ver AvisosPorJerarquia): primero al supervisor, con registro para el administrador.
public class AlertasDeFichaje
{
    public const string Biometria = "biometria";
    public const string Cara = "cara";

    // Para que un teléfono que insiste (o alguien que prueba la API) no llene de avisos al supervisor
    public static readonly TimeSpan EntreAvisos = TimeSpan.FromMinutes(5);

    // El servicio es un singleton: la memoria de "ya avisé" dura entre pedidos (y no se comparte entre pruebas)
    private readonly ConcurrentDictionary<string, DateTime> _ultimoAviso = new();
    private readonly ConcurrentDictionary<int, byte> _abiertasAvisadas = new();

    private readonly AvisosPorJerarquia _avisos;
    private readonly IEmpleadosRepositorio _empleados;
    private readonly JornadasService _jornadas;
    private readonly ILiquidacionRepositorio _liquidacion;
    private readonly TimeProvider _tiempo;

    public AlertasDeFichaje(AvisosPorJerarquia avisos, IEmpleadosRepositorio empleados, JornadasService jornadas,
        ILiquidacionRepositorio liquidacion, TimeProvider tiempo)
    {
        _avisos = avisos;
        _empleados = empleados;
        _jornadas = jornadas;
        _liquidacion = liquidacion;
        _tiempo = tiempo;
    }

    // Lo informa el teléfono cuando la huella o el control de cara fallan al fichar. false si no se avisó (tipo inválido o
    // ya se avisó hace poco).
    public async Task<bool> ReportarAsync(EmpleadoDto empleado, string? tipo)
    {
        var (titulo, texto) = (tipo ?? "").Trim().ToLowerInvariant() switch
        {
            Biometria => ("No pasó la huella", $"{empleado.NombreCompleto} intentó fichar y no pasó la verificación de huella."),
            Cara => ("Foto rechazada", $"{empleado.NombreCompleto} intentó fichar y la foto no mostraba una cara."),
            _ => (null, null)
        };
        if (titulo is null || texto is null) return false;

        var ahora = _tiempo.GetLocalNow().DateTime;
        var clave = $"{empleado.Id}:{tipo!.Trim().ToLowerInvariant()}";
        if (_ultimoAviso.TryGetValue(clave, out var antes) && ahora - antes < EntreAvisos) return false;
        _ultimoAviso[clave] = ahora;

        await _avisos.AvisarAsync(empleado, titulo, texto);
        return true;
    }

    // Jornadas con la entrada abierta por más horas de las permitidas: se avisa UNA vez por jornada. Devuelve cuántas.
    public async Task<int> RevisarFichajesAbiertosAsync()
    {
        var ahora = _tiempo.GetLocalNow().DateTime;
        var maxHoras = (await _liquidacion.ObtenerParametrosAsync()).MaxHorasFichajeAbierto;
        var personas = (await _empleados.ListarAsync()).Where(e => e.Activo).ToList();

        var (jornadas, _) = await _jornadas.ObtenerAsync(ahora.Date.AddDays(-3), ahora.Date);
        int avisos = 0;
        foreach (var j in jornadas.Where(j => j.EstaVencida(ahora, maxHoras)))
        {
            if (!_abiertasAvisadas.TryAdd(j.EntradaId, 0)) continue;
            var persona = personas.FirstOrDefault(p => p.NombreCompleto.Equals(j.Empleado, StringComparison.OrdinalIgnoreCase));
            if (persona is null) continue;

            await _avisos.AvisarAsync(persona, "Fichaje abierto",
                $"{persona.NombreCompleto} dejó la entrada del {j.Ingreso:dd/MM HH:mm} sin salida. Corregilo desde Administración → Corregir fichajes.");
            avisos++;
        }
        return avisos;
    }
}

// Cada 30 minutos
public class AlertasDeFichajeHostedService : BackgroundService
{
    private readonly IServiceProvider _servicios;
    private readonly ILogger<AlertasDeFichajeHostedService> _log;

    public AlertasDeFichajeHostedService(IServiceProvider servicios, ILogger<AlertasDeFichajeHostedService> log)
    {
        _servicios = servicios;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        using var reloj = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await reloj.WaitForNextTickAsync(cancelar))
        {
            try { await _servicios.GetRequiredService<AlertasDeFichaje>().RevisarFichajesAbiertosAsync(); }
            catch (Exception ex) { _log.LogWarning("No se pudo revisar los fichajes abiertos: {Tipo}", ex.GetType().Name); }
        }
    }
}
