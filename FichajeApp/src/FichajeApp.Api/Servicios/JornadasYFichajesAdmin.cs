using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Fuente única de jornadas (pares Entrada/Salida) para la liquidación, las correcciones y los informes.
// Equivale a PresenciaBLL de ingenieria_software.
public class JornadasService
{
    private readonly IFichajesRepositorio _fichajes;
    private readonly IEmpleadosRepositorio _empleados;

    public JornadasService(IFichajesRepositorio fichajes, IEmpleadosRepositorio empleados)
    {
        _fichajes = fichajes;
        _empleados = empleados;
    }

    // Jornadas que empezaron entre desde y hasta (inclusive). Se leen eventos de un día de margen para
    // no perder la salida de un turno nocturno que cruza la medianoche.
    public async Task<(List<Jornada> Jornadas, List<EventoFichaje> Huerfanos)> ObtenerAsync(
        DateTime desde, DateTime hasta, string? empleado = null)
    {
        var ids = (await _empleados.ListarAsync())
            .GroupBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var margenDesde = desde.Date.AddDays(-1);
        var margenHasta = hasta.Date.AddDays(2);

        var eventos = (await _fichajes.ListarAsync())
            .Where(f => f.FechaHora >= margenDesde && f.FechaHora < margenHasta)
            .Where(f => empleado == null || f.NombreCompleto.Equals(empleado, StringComparison.OrdinalIgnoreCase))
            .Select(f => new EventoFichaje(f.Id, f.NombreCompleto, f.FechaHora, f.Tipo, f.Corregido));

        var jornadas = ArmadoJornadas.Armar(eventos, n => ids.TryGetValue(n, out var id) ? id : 0, out var huerfanos);
        return (jornadas.Where(j => j.Fecha >= desde.Date && j.Fecha <= hasta.Date).ToList(),
                huerfanos.Where(h => h.FechaHora.Date >= desde.Date && h.FechaHora.Date <= hasta.Date).ToList());
    }
}

public class FichajeIncompletoDto
{
    public string Tipo { get; set; } = string.Empty; // "SinSalida" | "SalidaSinEntrada"
    public int EntradaId { get; set; }
    public int? SalidaId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public DateTime Ingreso { get; set; }
    public DateTime? Egreso { get; set; }
    public bool Vencida { get; set; }
}

// Revisión y corrección de fichajes (CU-39 del original). Corregir exige motivo, no permite superponer
// jornadas ni tocar períodos ya liquidados, y deja quién, cuándo y el valor anterior.
public class FichajesAdminService
{
    private const int LargoMaximoMotivo = 150;

    private readonly JornadasService _jornadas;
    private readonly IFichajesRepositorio _fichajes;
    private readonly IEmpleadosRepositorio _empleados;
    private readonly ILiquidacionRepositorio _liquidacion;
    private readonly BitacoraService _bitacora;
    private readonly TimeProvider _tiempo;

    public FichajesAdminService(JornadasService jornadas, IFichajesRepositorio fichajes, IEmpleadosRepositorio empleados,
        ILiquidacionRepositorio liquidacion, BitacoraService bitacora, TimeProvider tiempo)
    {
        _jornadas = jornadas;
        _fichajes = fichajes;
        _empleados = empleados;
        _liquidacion = liquidacion;
        _bitacora = bitacora;
        _tiempo = tiempo;
    }

    private DateTime Ahora => _tiempo.GetLocalNow().DateTime;

    public async Task<List<FichajeIncompletoDto>> ListarIncompletosAsync(DateTime desde, DateTime hasta, bool soloVencidos)
    {
        var parametros = await _liquidacion.ObtenerParametrosAsync();
        var (jornadas, huerfanos) = await _jornadas.ObtenerAsync(desde, hasta);

        var resultado = jornadas.Where(j => !j.EsCompleta)
            .Select(j => new FichajeIncompletoDto
            {
                Tipo = "SinSalida",
                EntradaId = j.EntradaId,
                Empleado = j.Empleado,
                Ingreso = j.Ingreso,
                Vencida = j.EstaVencida(Ahora, parametros.MaxHorasFichajeAbierto)
            }).ToList();

        if (soloVencidos) return resultado.Where(r => r.Vencida).ToList();

        resultado.AddRange(huerfanos.Select(h => new FichajeIncompletoDto
        {
            Tipo = "SalidaSinEntrada",
            EntradaId = h.Id,
            Empleado = h.Empleado,
            Ingreso = h.FechaHora,
            Vencida = true
        }));
        return resultado.OrderBy(r => r.Ingreso).ToList();
    }

    // Corrige el ingreso y el egreso de una jornada. Si no tenía salida, la agrega.
    public async Task CorregirAsync(string actor, int entradaId, DateTime nuevoIngreso, DateTime nuevoEgreso, string motivo)
    {
        motivo = ValidarMotivo(motivo);
        ValidarOrden(nuevoIngreso, nuevoEgreso);

        var entrada = await _fichajes.ObtenerAsync(entradaId);
        if (entrada is null || entrada.Tipo != "Entrada")
            throw new InvalidOperationException("La entrada indicada no existe.");

        await ValidarPeriodoAbiertoAsync(entrada.FechaHora.Date, nuevoIngreso.Date, nuevoEgreso.Date);

        var (jornadas, _) = await _jornadas.ObtenerAsync(entrada.FechaHora.Date, entrada.FechaHora.Date, entrada.NombreCompleto);
        var jornada = jornadas.FirstOrDefault(j => j.EntradaId == entradaId)
            ?? throw new InvalidOperationException("No se encontró la jornada de ese fichaje.");

        await ValidarSinSuperposicionAsync(entrada.NombreCompleto, nuevoIngreso, nuevoEgreso, entradaId);

        await _fichajes.CorregirFechaHoraAsync(entradaId, nuevoIngreso, motivo, actor, Ahora);
        if (jornada.SalidaId.HasValue)
            await _fichajes.CorregirFechaHoraAsync(jornada.SalidaId.Value, nuevoEgreso, motivo, actor, Ahora);
        else
            await _fichajes.AgregarManualAsync(entrada.NombreCompleto, "Salida", nuevoEgreso, motivo, actor, Ahora);

        await _bitacora.RegistrarAsync(actor, "FICHAJE_CORREGIDO:" + entrada.NombreCompleto);
    }

    // Una salida sin entrada no se puede pagar ni se puede "corregir" (no hay entrada que tocar):
    // se agrega la entrada que faltó.
    public async Task CompletarEntradaAsync(string actor, int salidaId, DateTime ingreso, string motivo)
    {
        motivo = ValidarMotivo(motivo);

        var salida = await _fichajes.ObtenerAsync(salidaId);
        if (salida is null || salida.Tipo != "Salida")
            throw new InvalidOperationException("La salida indicada no existe.");

        var (_, huerfanos) = await _jornadas.ObtenerAsync(salida.FechaHora.Date, salida.FechaHora.Date, salida.NombreCompleto);
        if (huerfanos.All(h => h.Id != salidaId))
            throw new InvalidOperationException("Esa salida ya tiene su entrada.");

        ValidarOrden(ingreso, salida.FechaHora);
        await ValidarPeriodoAbiertoAsync(ingreso.Date, salida.FechaHora.Date);
        await ValidarSinSuperposicionAsync(salida.NombreCompleto, ingreso, salida.FechaHora, ignorarEntradaId: 0);

        await _fichajes.AgregarManualAsync(salida.NombreCompleto, "Entrada", ingreso, motivo, actor, Ahora);
        await _bitacora.RegistrarAsync(actor, "FICHAJE_CORREGIDO:" + salida.NombreCompleto);
    }

    // Carga una jornada completa que nunca se fichó (por ejemplo, se cortó el celular)
    public async Task AgregarManualAsync(string actor, string empleado, DateTime ingreso, DateTime egreso, string motivo)
    {
        motivo = ValidarMotivo(motivo);
        ValidarOrden(ingreso, egreso);
        if (ingreso.Date > Ahora.Date)
            throw new InvalidOperationException("No se puede cargar un fichaje con fecha futura.");

        var persona = (await _empleados.ListarAsync())
            .FirstOrDefault(e => e.NombreCompleto.Equals(empleado?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El empleado no existe.");

        await ValidarPeriodoAbiertoAsync(ingreso.Date, egreso.Date);
        await ValidarSinSuperposicionAsync(persona.NombreCompleto, ingreso, egreso, ignorarEntradaId: 0);

        await _fichajes.AgregarManualAsync(persona.NombreCompleto, "Entrada", ingreso, motivo, actor, Ahora);
        await _fichajes.AgregarManualAsync(persona.NombreCompleto, "Salida", egreso, motivo, actor, Ahora);
        await _bitacora.RegistrarAsync(actor, "FICHAJE_MANUAL:" + persona.NombreCompleto);
    }

    private static string ValidarMotivo(string? motivo)
    {
        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) throw new InvalidOperationException("Indicá el motivo de la corrección.");
        if (motivo.Length > LargoMaximoMotivo)
            throw new InvalidOperationException($"El motivo no puede superar los {LargoMaximoMotivo} caracteres.");
        return motivo;
    }

    private static void ValidarOrden(DateTime ingreso, DateTime egreso)
    {
        if (egreso <= ingreso)
            throw new InvalidOperationException("El egreso tiene que ser posterior al ingreso.");
        if ((egreso - ingreso).TotalHours > 24)
            throw new InvalidOperationException("Una jornada no puede durar más de 24 horas.");
    }

    private async Task ValidarPeriodoAbiertoAsync(params DateTime[] fechas)
    {
        foreach (var f in fechas.Distinct())
            if (await _liquidacion.HayLiquidacionEnRangoAsync(f, f))
                throw new InvalidOperationException("Esa fecha pertenece a un período ya liquidado.");
    }

    private async Task ValidarSinSuperposicionAsync(string empleado, DateTime inicio, DateTime fin, int ignorarEntradaId)
    {
        var (jornadas, _) = await _jornadas.ObtenerAsync(inicio.Date.AddDays(-1), fin.Date, empleado);
        foreach (var otra in jornadas)
        {
            if (otra.EntradaId == ignorarEntradaId || !otra.Egreso.HasValue) continue;
            if (inicio < otra.Egreso.Value && fin > otra.Ingreso)
                throw new InvalidOperationException("El fichaje se superpone con otro del mismo empleado.");
        }
    }
}
