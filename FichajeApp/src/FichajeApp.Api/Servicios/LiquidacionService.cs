using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

public class ValorHoraDto
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateTime VigenteDesde { get; set; }
}

// Liquidación de horas (CU-38 del original): calcula lo que corresponde pagar por período, permite cerrar
// el período (foto que ya no se recalcula) y marcarlo como pagado.
public class LiquidacionService
{
    private const int MaxDiasPeriodo = 62;
    private static readonly SemaphoreSlim _cierre = new(1, 1);

    private readonly JornadasService _jornadas;
    private readonly ILiquidacionRepositorio _repositorio;
    private readonly IEmpleadosRepositorio _empleados;
    private readonly BitacoraService _bitacora;
    private readonly TimeProvider _tiempo;

    public LiquidacionService(JornadasService jornadas, ILiquidacionRepositorio repositorio, IEmpleadosRepositorio empleados,
        BitacoraService bitacora, TimeProvider tiempo)
    {
        _jornadas = jornadas;
        _repositorio = repositorio;
        _empleados = empleados;
        _bitacora = bitacora;
        _tiempo = tiempo;
    }

    private DateTime Ahora => _tiempo.GetLocalNow().DateTime;

    public async Task<List<LiquidacionDia>> CalcularAsync(DateTime desde, DateTime hasta, string? empleado = null)
    {
        ValidarRango(desde, hasta);

        var (jornadas, _) = await _jornadas.ObtenerAsync(desde, hasta, empleado);
        var parametros = await _repositorio.ObtenerParametrosAsync();
        var entrada = new EntradaLiquidacion
        {
            Jornadas = jornadas,
            ValoresHora = await _repositorio.ListarValoresHoraAsync(),
            Feriados = (await _repositorio.ListarFeriadosAsync()).Where(f => f.PagaRecargo(parametros.PagarPuentes))
                .Select(f => f.Fecha.Date).ToHashSet(),
            ExcedentesAprobados = (await _repositorio.ListarExcedentesAprobadosAsync())
                .Select(x => (x.Empleado.ToLowerInvariant(), x.Fecha.Date)).ToHashSet(),
            Parametros = parametros,
            Ahora = Ahora
        };
        return LiquidacionCalculo.Calcular(entrada, desde, hasta);
    }

    public static List<ResumenEmpleado> Resumir(IEnumerable<LiquidacionDia> dias) => LiquidacionCalculo.Resumir(dias);

    public async Task<PeriodoLiquidado> CerrarAsync(string actor, DateTime desde, DateTime hasta)
    {
        ValidarRango(desde, hasta);
        // Se puede liquidar hasta hoy; si alguien sigue adentro (entrada sin salida) el chequeo de incompletos lo frena
        if (hasta.Date > Ahora.Date)
            throw new InvalidOperationException("No se puede liquidar una fecha futura.");

        await _cierre.WaitAsync();
        try
        {
            if (await _repositorio.HayLiquidacionEnRangoAsync(desde, hasta))
                throw new InvalidOperationException("Ya existe una liquidación que se superpone con ese período.");

            var (jornadas, huerfanos) = await _jornadas.ObtenerAsync(desde, hasta);
            int incompletos = jornadas.Count(j => !j.EsCompleta) + huerfanos.Count;
            if (incompletos > 0)
                throw new InvalidOperationException(
                    $"Hay {incompletos} fichaje(s) incompleto(s) en el período. Corregilos antes de liquidar.");

            var dias = await CalcularAsync(desde, hasta);
            if (dias.Count == 0)
                throw new InvalidOperationException("No hay horas para liquidar en el período.");

            var sinValor = dias.Where(d => d.MinPagables > 0 && d.ValorHora == 0m).Select(d => d.Empleado).Distinct().ToList();
            if (sinValor.Count > 0)
                throw new InvalidOperationException("Falta cargar el valor hora de: " + string.Join(", ", sinValor) + ".");

            var periodo = new PeriodoLiquidado
            {
                Desde = desde.Date,
                Hasta = hasta.Date,
                Estado = "Cerrada",
                CerradaPor = actor,
                FechaCierre = Ahora
            };
            await _repositorio.GuardarLiquidacionAsync(periodo, dias);
            await _bitacora.RegistrarAsync(actor, $"LIQUIDACION_CERRADA:{desde:yyyyMMdd}-{hasta:yyyyMMdd}");
            return periodo;
        }
        finally { _cierre.Release(); }
    }

    public async Task MarcarPagadaAsync(string actor, int liquidacionId)
    {
        await _cierre.WaitAsync();
        try
        {
            var l = (await _repositorio.ListarLiquidacionesAsync()).FirstOrDefault(x => x.Id == liquidacionId)
                ?? throw new InvalidOperationException("La liquidación no existe.");
            if (l.Estado != "Cerrada")
                throw new InvalidOperationException("Solo se puede marcar como pagada una liquidación cerrada.");

            await _repositorio.MarcarPagadaAsync(liquidacionId, actor, Ahora);
            await _bitacora.RegistrarAsync(actor, $"LIQUIDACION_PAGADA:{l.Desde:yyyyMMdd}-{l.Hasta:yyyyMMdd}");
        }
        finally { _cierre.Release(); }
    }

    public Task<List<PeriodoLiquidado>> ListarLiquidacionesAsync() => _repositorio.ListarLiquidacionesAsync();

    public Task<List<LiquidacionDia>> ObtenerDiasDeLiquidacionAsync(int liquidacionId) =>
        _repositorio.ListarDiasDeLiquidacionAsync(liquidacionId);

    // ---- Excedentes (solo aplican cuando hay turnos asignados) ----

    public async Task AprobarExcedenteAsync(string actor, string empleado, DateTime fecha)
    {
        await ValidarPeriodoAbiertoAsync(fecha);
        await _repositorio.AprobarExcedenteAsync(empleado, fecha.Date, actor);
        await _bitacora.RegistrarAsync(actor, $"EXCEDENTE_APROBADO:{fecha:yyyyMMdd} {empleado}");
    }

    public async Task RevocarExcedenteAsync(string actor, string empleado, DateTime fecha)
    {
        await ValidarPeriodoAbiertoAsync(fecha);
        await _repositorio.RevocarExcedenteAsync(empleado, fecha.Date);
        await _bitacora.RegistrarAsync(actor, $"EXCEDENTE_REVOCADO:{fecha:yyyyMMdd} {empleado}");
    }

    // ---- Parámetros, valor hora y feriados ----

    public Task<ParametrosLiquidacion> ObtenerParametrosAsync() => _repositorio.ObtenerParametrosAsync();

    public async Task GuardarParametrosAsync(string actor, ParametrosLiquidacion p)
    {
        if (p.ToleranciaMinutos < 0 || p.ToleranciaMinutos > 60)
            throw new InvalidOperationException("La tolerancia debe estar entre 0 y 60 minutos.");
        if (p.RedondeoMinutos < 0 || p.RedondeoMinutos > 60)
            throw new InvalidOperationException("El redondeo debe estar entre 0 y 60 minutos.");
        if (p.JornadaNormalHoras < 1 || p.JornadaNormalHoras > 24)
            throw new InvalidOperationException("La jornada normal debe estar entre 1 y 24 horas.");
        if (p.RecargoExtraPct < 0 || p.RecargoExtraPct > 300 || p.RecargoFeriadoPct < 0 || p.RecargoFeriadoPct > 300)
            throw new InvalidOperationException("Los recargos deben estar entre 0% y 300%.");
        if (p.MaxHorasFichajeAbierto < 1 || p.MaxHorasFichajeAbierto > 24)
            throw new InvalidOperationException("El máximo de horas de una entrada abierta debe estar entre 1 y 24.");

        await _repositorio.GuardarParametrosAsync(p);
        await _bitacora.RegistrarAsync(actor, "PARAMETROS_LIQUIDACION_MODIFICADOS");
    }

    public async Task<List<ValorHoraDto>> ListarValoresHoraAsync()
    {
        var nombres = (await _empleados.ListarAsync()).ToDictionary(e => e.Id, e => e.NombreCompleto);
        return (await _repositorio.ListarValoresHoraAsync())
            .Select(v => new ValorHoraDto
            {
                EmpleadoId = v.EmpleadoId,
                Empleado = nombres.GetValueOrDefault(v.EmpleadoId, "(desconocido)"),
                Valor = v.Valor,
                VigenteDesde = v.VigenteDesde
            })
            .OrderBy(v => v.Empleado, StringComparer.OrdinalIgnoreCase).ThenByDescending(v => v.VigenteDesde).ToList();
    }

    public async Task GuardarValorHoraAsync(string actor, int empleadoId, decimal valor, DateTime vigenteDesde)
    {
        if (valor <= 0m)
            throw new InvalidOperationException("El valor hora debe ser mayor a cero.");
        var empleado = await _empleados.ObtenerPorIdAsync(empleadoId)
            ?? throw new InvalidOperationException("El empleado no existe.");

        await _repositorio.GuardarValorHoraAsync(new ValorHoraEmpleado
        {
            EmpleadoId = empleadoId,
            Valor = Math.Round(valor, 2),
            VigenteDesde = vigenteDesde.Date,
            CargadoPor = actor
        });
        await _bitacora.RegistrarAsync(actor, "VALOR_HORA_MODIFICADO:" + empleado.NombreCompleto);
    }

    public async Task<List<Feriado>> ListarFeriadosAsync(DateTime desde, DateTime hasta) =>
        (await _repositorio.ListarFeriadosAsync()).Where(f => f.Fecha >= desde.Date && f.Fecha <= hasta.Date).ToList();

    public async Task GuardarFeriadoAsync(string actor, DateTime fecha, string descripcion)
    {
        await _repositorio.GuardarFeriadoAsync(new Feriado { Fecha = fecha.Date, Descripcion = (descripcion ?? "").Trim(), Origen = Feriado.OrigenManual });
        await _bitacora.RegistrarAsync(actor, "FERIADO_GUARDADO:" + fecha.ToString("yyyyMMdd"));
    }

    public async Task EliminarFeriadoAsync(string actor, DateTime fecha)
    {
        await _repositorio.EliminarFeriadoAsync(fecha.Date);
        await _bitacora.RegistrarAsync(actor, "FERIADO_ELIMINADO:" + fecha.ToString("yyyyMMdd"));
    }

    private static void ValidarRango(DateTime desde, DateTime hasta)
    {
        if (desde.Date > hasta.Date)
            throw new InvalidOperationException("La fecha desde no puede ser posterior a la fecha hasta.");
        if ((hasta.Date - desde.Date).TotalDays > MaxDiasPeriodo)
            throw new InvalidOperationException($"El período no puede superar los {MaxDiasPeriodo} días.");
    }

    private async Task ValidarPeriodoAbiertoAsync(DateTime fecha)
    {
        if (await _repositorio.HayLiquidacionEnRangoAsync(fecha, fecha))
            throw new InvalidOperationException("Esa fecha pertenece a un período ya liquidado.");
    }
}
