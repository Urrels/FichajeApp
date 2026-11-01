using System.Globalization;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Repositorios;

// Hojas: Parametros, ValorHora, Feriados, ExcedentesAprobados, Liquidaciones y LiquidacionDias.
// Las escrituras de más de una fila (cerrar una liquidación) no son atómicas como en SQL:
// se serializan con un lock y se escribe primero el detalle y recién después el encabezado,
// así un corte a mitad de camino no deja un período "cerrado" sin sus días.
public class LiquidacionSheetsRepositorio : ILiquidacionRepositorio
{
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly HojaSheets _parametros, _valorHora, _feriados, _excedentes, _liquidaciones, _dias;

    public LiquidacionSheetsRepositorio(GoogleSheetsClient cliente)
    {
        _parametros = new HojaSheets(cliente, Planilla.Parametros);
        _valorHora = new HojaSheets(cliente, Planilla.ValorHora);
        _feriados = new HojaSheets(cliente, Planilla.Feriados);
        _excedentes = new HojaSheets(cliente, Planilla.ExcedentesAprobados);
        _liquidaciones = new HojaSheets(cliente, Planilla.Liquidaciones);
        _dias = new HojaSheets(cliente, Planilla.LiquidacionDias);
    }

    // ---- Parámetros ----

    public static readonly (string Clave, decimal Valor, string Descripcion)[] ParametrosIniciales =
    {
        ("TOLERANCIA_MINUTOS", 10, "Con turnos: diferencia (min) entre lo fichado y lo asignado que no se descuenta ni se suma"),
        ("REDONDEO_MINUTOS", 15, "Las horas de cada día se redondean al múltiplo más cercano de estos minutos (0 = sin redondeo)"),
        ("JORNADA_NORMAL_HORAS", 8, "Horas por día a valor normal; lo que excede se paga con recargo de hora extra"),
        ("RECARGO_EXTRA_PCT", 50, "Recargo (%) de las horas extra de días comunes"),
        ("RECARGO_FERIADO_PCT", 100, "Recargo (%) de las horas trabajadas en domingo o feriado"),
        ("MAX_HORAS_FICHAJE_ABIERTO", 16, "Una entrada sin salida con más horas que estas se considera incompleta y hay que corregirla"),
        ("PAGAR_PUENTES", 0, "1 = los días puente y no laborables también se pagan con el recargo de feriado; 0 = solo los feriados obligatorios")
    };

    public async Task<ParametrosLiquidacion> ObtenerParametrosAsync()
    {
        var valores = (await _parametros.LeerAsync("A:C")).ToDictionary(f => f.Texto(0), f => f.Decimal(1));
        decimal Valor(string clave, decimal defecto) => valores.TryGetValue(clave, out var v) ? v : defecto;

        return new ParametrosLiquidacion
        {
            ToleranciaMinutos = (int)Valor("TOLERANCIA_MINUTOS", 10),
            RedondeoMinutos = (int)Valor("REDONDEO_MINUTOS", 15),
            JornadaNormalHoras = Valor("JORNADA_NORMAL_HORAS", 8),
            RecargoExtraPct = Valor("RECARGO_EXTRA_PCT", 50),
            RecargoFeriadoPct = Valor("RECARGO_FERIADO_PCT", 100),
            MaxHorasFichajeAbierto = (int)Valor("MAX_HORAS_FICHAJE_ABIERTO", 16),
            PagarPuentes = Valor("PAGAR_PUENTES", 0) != 0
        };
    }

    public async Task GuardarParametrosAsync(ParametrosLiquidacion p)
    {
        await _escritura.WaitAsync();
        try
        {
            var nuevos = new Dictionary<string, decimal>
            {
                ["TOLERANCIA_MINUTOS"] = p.ToleranciaMinutos,
                ["REDONDEO_MINUTOS"] = p.RedondeoMinutos,
                ["JORNADA_NORMAL_HORAS"] = p.JornadaNormalHoras,
                ["RECARGO_EXTRA_PCT"] = p.RecargoExtraPct,
                ["RECARGO_FERIADO_PCT"] = p.RecargoFeriadoPct,
                ["MAX_HORAS_FICHAJE_ABIERTO"] = p.MaxHorasFichajeAbierto,
                ["PAGAR_PUENTES"] = p.PagarPuentes ? 1 : 0
            };

            var actuales = await _parametros.LeerAsync("A:C");
            foreach (var (clave, valor) in nuevos)
            {
                var fila = actuales.FirstOrDefault(f => f.Texto(0) == clave);
                var descripcion = fila?.Texto(2) ?? ParametrosIniciales.First(x => x.Clave == clave).Descripcion;
                var datos = new List<object> { clave, (double)valor, descripcion };
                if (fila != null) await _parametros.ActualizarAsync(fila.Numero, datos);
                else await _parametros.AgregarAsync(datos);
            }
        }
        finally { _escritura.Release(); }
    }

    // ---- Valor hora ----

    public async Task<List<ValorHoraEmpleado>> ListarValoresHoraAsync() =>
        (await _valorHora.LeerAsync("A:D")).Select(f => new ValorHoraEmpleado
        {
            EmpleadoId = f.Entero(0),
            Valor = f.Decimal(1),
            VigenteDesde = f.Fecha(2),
            CargadoPor = f.Texto(3)
        }).ToList();

    public async Task GuardarValorHoraAsync(ValorHoraEmpleado v)
    {
        await _escritura.WaitAsync();
        try
        {
            var datos = new List<object> { v.EmpleadoId, (double)v.Valor, HojaSheets.Fecha(v.VigenteDesde), v.CargadoPor ?? "" };

            // Un empleado solo puede tener un valor por fecha de vigencia
            var existente = (await _valorHora.LeerAsync("A:D"))
                .FirstOrDefault(f => f.Entero(0) == v.EmpleadoId && f.Fecha(2).Date == v.VigenteDesde.Date);
            if (existente != null) await _valorHora.ActualizarAsync(existente.Numero, datos);
            else await _valorHora.AgregarAsync(datos);
        }
        finally { _escritura.Release(); }
    }

    // ---- Feriados ----

    // Una fecha ilegible (año 1) se descarta y, si una fecha está repetida a mano, vale la última fila
    private static List<Feriado> LeerFeriados(IEnumerable<FilaHoja> filas) =>
        filas.Select(f => new Feriado
            {
                Fecha = f.Fecha(0).Date, Descripcion = f.Texto(1), Tipo = f.Texto(2),
                Origen = f.Texto(3) switch
                {
                    Feriado.OrigenApi => Feriado.OrigenApi,
                    Feriado.OrigenExcluido => Feriado.OrigenExcluido,
                    _ => Feriado.OrigenManual
                }
            })
            .Where(f => f.Fecha.Year >= 2000)
            .GroupBy(f => f.Fecha).Select(g => g.Last())
            .OrderBy(f => f.Fecha).ToList();

    private static IList<object> FilaDe(Feriado f) =>
        new List<object> { HojaSheets.Fecha(f.Fecha), f.Descripcion, f.Tipo, f.Origen };

    public async Task<List<Feriado>> ListarFeriadosAsync() => LeerFeriados(await _feriados.LeerAsync("A:D"));

    public async Task<ResultadoSincronizacion> AplicarOficialesAsync(IReadOnlyCollection<Feriado> oficiales, DateTime hoy)
    {
        // Se lee y se escribe dentro del mismo lock: un feriado que alguien cargue a mano mientras tanto no se pisa
        await _escritura.WaitAsync();
        try
        {
            var actuales = LeerFeriados(await _feriados.LeerAsync("A:D"));
            var plan = FusionFeriados.Planificar(actuales, oficiales, hoy);

            if (plan.Actualizar.Count + plan.Retirar.Count > 0)
                await _feriados.ReemplazarTodoAsync(FusionFeriados.Aplicar(actuales, plan).Select(FilaDe));
            else
                await _feriados.AgregarVariasAsync(plan.Agregar.Select(FilaDe));

            return FusionFeriados.Resumen(plan);
        }
        finally { _escritura.Release(); }
    }

    public async Task GuardarFeriadoAsync(Feriado feriado)
    {
        await _escritura.WaitAsync();
        try
        {
            var datos = new List<object> { HojaSheets.Fecha(feriado.Fecha), feriado.Descripcion, feriado.Tipo, feriado.Origen };
            var existente = (await _feriados.LeerAsync("A:D")).FirstOrDefault(f => f.Fecha(0).Date == feriado.Fecha.Date);
            if (existente != null) await _feriados.ActualizarAsync(existente.Numero, datos);
            else await _feriados.AgregarAsync(datos);
        }
        finally { _escritura.Release(); }
    }

    public async Task EliminarFeriadoAsync(DateTime fecha)
    {
        await _escritura.WaitAsync();
        try
        {
            var resto = (await _feriados.LeerAsync("A:D")).Where(f => f.Fecha(0).Date != fecha.Date)
                .Select(f => (IList<object>)new List<object> { HojaSheets.Fecha(f.Fecha(0)), f.Texto(1), f.Texto(2), f.Texto(3) }).ToList();
            await _feriados.ReemplazarTodoAsync(resto);
        }
        finally { _escritura.Release(); }
    }

    // ---- Excedentes aprobados ----

    public async Task<List<(string Empleado, DateTime Fecha)>> ListarExcedentesAprobadosAsync() =>
        (await _excedentes.LeerAsync("A:C")).Select(f => (f.Texto(0), f.Fecha(1).Date)).ToList();

    public async Task AprobarExcedenteAsync(string empleado, DateTime fecha, string aprobadoPor)
    {
        await _escritura.WaitAsync();
        try
        {
            var ya = (await _excedentes.LeerAsync("A:C"))
                .Any(f => f.Texto(0).Equals(empleado, StringComparison.OrdinalIgnoreCase) && f.Fecha(1).Date == fecha.Date);
            if (!ya) await _excedentes.AgregarAsync(new List<object> { empleado, HojaSheets.Fecha(fecha), aprobadoPor });
        }
        finally { _escritura.Release(); }
    }

    public async Task RevocarExcedenteAsync(string empleado, DateTime fecha)
    {
        await _escritura.WaitAsync();
        try
        {
            var resto = (await _excedentes.LeerAsync("A:C"))
                .Where(f => !(f.Texto(0).Equals(empleado, StringComparison.OrdinalIgnoreCase) && f.Fecha(1).Date == fecha.Date))
                .Select(f => (IList<object>)new List<object> { f.Texto(0), HojaSheets.Fecha(f.Fecha(1)), f.Texto(2) }).ToList();
            await _excedentes.ReemplazarTodoAsync(resto);
        }
        finally { _escritura.Release(); }
    }

    // ---- Liquidaciones ----

    public async Task<List<PeriodoLiquidado>> ListarLiquidacionesAsync() =>
        (await _liquidaciones.LeerAsync("A:H")).Select(f => new PeriodoLiquidado
        {
            Id = f.Entero(0),
            Desde = f.Fecha(1).Date,
            Hasta = f.Fecha(2).Date,
            Estado = f.Texto(3),
            CerradaPor = f.Texto(4),
            FechaCierre = f.Fecha(5),
            PagadaPor = string.IsNullOrWhiteSpace(f.Texto(6)) ? null : f.Texto(6),
            FechaPago = f.FechaONula(7)
        }).OrderByDescending(l => l.Desde).ToList();

    public async Task<bool> HayLiquidacionEnRangoAsync(DateTime desde, DateTime hasta) =>
        (await ListarLiquidacionesAsync()).Any(l => l.Desde.Date <= hasta.Date && l.Hasta.Date >= desde.Date);

    public async Task<int> GuardarLiquidacionAsync(PeriodoLiquidado p, IReadOnlyCollection<LiquidacionDia> dias)
    {
        await _escritura.WaitAsync();
        try
        {
            var existentes = await ListarLiquidacionesAsync();
            if (existentes.Any(l => l.Desde.Date <= p.Hasta.Date && l.Hasta.Date >= p.Desde.Date))
                throw new InvalidOperationException("Ya existe una liquidación que se superpone con ese período.");

            p.Id = existentes.Count == 0 ? 1 : existentes.Max(l => l.Id) + 1;

            // Primero el detalle y después el encabezado: el encabezado es lo que "cierra" el período
            foreach (var d in dias)
            {
                await _dias.AgregarAsync(new List<object>
                {
                    p.Id, d.EmpleadoId, d.Empleado, HojaSheets.Fecha(d.Fecha),
                    d.Ingreso.HasValue ? HojaSheets.FechaHora(d.Ingreso.Value) : "",
                    d.Egreso.HasValue ? HojaSheets.FechaHora(d.Egreso.Value) : "",
                    d.MinTrabajados, d.MinAsignados, d.MinPagables, d.MinNormales, d.MinExtra, d.MinFeriado, d.MinExcedente,
                    d.ExcedenteAprobado, d.Ausente, (double)d.ValorHora, (double)d.RecargoExtraPct, (double)d.RecargoFeriadoPct,
                    (double)d.Importe, d.Observacion ?? ""
                });
            }

            await _liquidaciones.AgregarAsync(new List<object>
            {
                p.Id, HojaSheets.Fecha(p.Desde), HojaSheets.Fecha(p.Hasta), p.Estado, p.CerradaPor,
                HojaSheets.FechaHora(p.FechaCierre), "", ""
            });
            return p.Id;
        }
        finally { _escritura.Release(); }
    }

    public async Task MarcarPagadaAsync(int liquidacionId, string pagadaPor, DateTime fechaPago)
    {
        await _escritura.WaitAsync();
        try
        {
            var fila = (await _liquidaciones.LeerAsync("A:H")).FirstOrDefault(f => f.Entero(0) == liquidacionId)
                ?? throw new InvalidOperationException("La liquidación no existe.");

            await _liquidaciones.ActualizarAsync(fila.Numero, new List<object>
            {
                liquidacionId, HojaSheets.Fecha(fila.Fecha(1)), HojaSheets.Fecha(fila.Fecha(2)), "Pagada",
                fila.Texto(4), HojaSheets.FechaHora(fila.Fecha(5)), pagadaPor, HojaSheets.FechaHora(fechaPago)
            });
        }
        finally { _escritura.Release(); }
    }

    public async Task<List<LiquidacionDia>> ListarDiasDeLiquidacionAsync(int liquidacionId) =>
        (await _dias.LeerAsync("A:T")).Where(f => f.Entero(0) == liquidacionId).Select(f => new LiquidacionDia
        {
            EmpleadoId = f.Entero(1),
            Empleado = f.Texto(2),
            Fecha = f.Fecha(3).Date,
            Ingreso = f.FechaONula(4),
            Egreso = f.FechaONula(5),
            MinTrabajados = f.Entero(6),
            MinAsignados = f.Entero(7),
            MinPagables = f.Entero(8),
            MinNormales = f.Entero(9),
            MinExtra = f.Entero(10),
            MinFeriado = f.Entero(11),
            MinExcedente = f.Entero(12),
            ExcedenteAprobado = f.Booleano(13),
            Ausente = f.Booleano(14),
            ValorHora = f.Decimal(15),
            RecargoExtraPct = f.Decimal(16),
            RecargoFeriadoPct = f.Decimal(17),
            Importe = f.Decimal(18),
            Observacion = string.IsNullOrWhiteSpace(f.Texto(19)) ? null : f.Texto(19)
        }).OrderBy(d => d.Empleado, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Fecha).ToList();
}
