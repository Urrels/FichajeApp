namespace FichajeApp.Api.Dominio.Liquidacion;

// Cálculo de horas y pago, sin acceso a datos (se prueba con listas en memoria).
// Portado de BLL.LiquidacionCalculo de ingenieria_software.
//
// Por empleado y día: horas fichadas (solo jornadas completas) → redondeo → a pagar
// → normales hasta la jornada normal, extra con recargo, domingos y feriados con recargo
// → importe con el valor hora vigente ese día.
//
// Dos modos, según haya o no turnos asignados:
//  - Sin turnos (el caso de FichajeApp hoy): se paga lo fichado.
//  - Con turnos: se paga el mínimo entre lo fichado y lo asignado; el excedente solo si un
//    administrador lo aprobó. Una diferencia dentro de la tolerancia cuenta como lo asignado.
public static class LiquidacionCalculo
{
    public static string Hhmm(int minutos)
    {
        int m = Math.Abs(minutos);
        return $"{(minutos < 0 ? "-" : "")}{m / 60}:{m % 60:00}";
    }

    public static decimal Horas(int minutos) => Math.Round(minutos / 60m, 2, MidpointRounding.AwayFromZero);

    public static int Redondear(int minutos, int multiplo)
    {
        if (multiplo <= 0) return minutos;
        return (int)(Math.Round(minutos / (double)multiplo, MidpointRounding.AwayFromZero) * multiplo);
    }

    public static decimal ValorHoraVigente(IEnumerable<ValorHoraEmpleado> valores, int empleadoId, DateTime fecha)
    {
        var vigente = valores
            .Where(v => v.EmpleadoId == empleadoId && v.VigenteDesde.Date <= fecha.Date)
            .OrderByDescending(v => v.VigenteDesde)
            .FirstOrDefault();
        return vigente?.Valor ?? 0m;
    }

    public static List<LiquidacionDia> Calcular(EntradaLiquidacion e, DateTime desde, DateTime hasta)
    {
        var p = e.Parametros;
        desde = desde.Date;
        hasta = hasta.Date;

        var jornadas = e.Jornadas.Where(j => j.Fecha >= desde && j.Fecha <= hasta).ToList();
        var turnos = e.Turnos.Where(t => t.Fecha >= desde && t.Fecha <= hasta).ToList();
        bool usaTurnos = e.Turnos.Count > 0;

        var claves = jornadas.Select(j => (Empleado: j.Empleado.ToLowerInvariant(), j.Fecha))
            .Union(turnos.Select(t => (Empleado: t.Empleado.ToLowerInvariant(), t.Fecha)))
            .ToList();

        var resultado = new List<LiquidacionDia>();
        foreach (var clave in claves)
        {
            var jornadasDia = jornadas.Where(j => j.Empleado.ToLowerInvariant() == clave.Empleado && j.Fecha == clave.Fecha).ToList();
            var turnosDia = turnos.Where(t => t.Empleado.ToLowerInvariant() == clave.Empleado && t.Fecha == clave.Fecha).ToList();
            var completas = jornadasDia.Where(j => j.EsCompleta).ToList();

            string nombre = jornadasDia.FirstOrDefault()?.Empleado ?? turnosDia.First().Empleado;
            int empleadoId = jornadasDia.FirstOrDefault()?.EmpleadoId ?? 0;

            int crudo = completas.Sum(j => j.MinutosTrabajados);
            int asignados = turnosDia.Sum(t => t.Minutos);
            bool hayIncompleta = jornadasDia.Any(j => !j.EsCompleta);

            // Un turno que todavía no terminó no genera fila
            if (jornadasDia.Count == 0 && turnosDia.Any(t => t.Fin > e.Ahora)) continue;

            int efectivo, excedente, pagables;
            bool aprobado = false;

            if (!usaTurnos)
            {
                efectivo = Redondear(crudo, p.RedondeoMinutos);
                excedente = 0;
                pagables = efectivo;
            }
            else
            {
                efectivo = crudo;
                if (asignados > 0 && Math.Abs(crudo - asignados) <= p.ToleranciaMinutos) efectivo = asignados;
                efectivo = Redondear(efectivo, p.RedondeoMinutos);

                // Un día sin turno es todo excedente
                excedente = asignados > 0 ? Math.Max(0, efectivo - asignados) : efectivo;
                aprobado = excedente > 0 && e.ExcedentesAprobados.Contains((clave.Empleado, clave.Fecha));
                pagables = Math.Min(efectivo, asignados) + (aprobado ? excedente : 0);
            }

            bool ausente = asignados > 0 && crudo == 0 && !hayIncompleta;

            bool esFeriado = clave.Fecha.DayOfWeek == DayOfWeek.Sunday || e.Feriados.Contains(clave.Fecha);
            int normales = 0, extra = 0, feriado = 0;
            if (esFeriado)
            {
                feriado = pagables;
            }
            else
            {
                int jornada = (int)Math.Round(p.JornadaNormalHoras * 60);
                normales = Math.Min(pagables, jornada);
                extra = pagables - normales;
            }

            decimal valor = ValorHoraVigente(e.ValoresHora, empleadoId, clave.Fecha);
            decimal importe = Math.Round(valor *
                (normales + extra * (1 + p.RecargoExtraPct / 100m) + feriado * (1 + p.RecargoFeriadoPct / 100m)) / 60m,
                2, MidpointRounding.AwayFromZero);

            var observaciones = new List<string>();
            if (hayIncompleta) observaciones.Add("Fichaje incompleto");
            if (ausente) observaciones.Add("Ausente");
            if (usaTurnos && asignados == 0 && efectivo > 0) observaciones.Add("Sin turno asignado");
            if (excedente > 0)
                observaciones.Add(aprobado ? "Excedente aprobado " + Hhmm(excedente) : "Excedente a aprobar " + Hhmm(excedente));
            if (esFeriado && pagables > 0) observaciones.Add("Domingo o feriado");
            if (jornadasDia.Any(j => j.Corregido)) observaciones.Add("Fichaje corregido");
            if (valor == 0m && pagables > 0) observaciones.Add("Sin valor hora");
            string observacion = string.Join("; ", observaciones);
            if (observacion.Length > 200) observacion = observacion[..200];

            resultado.Add(new LiquidacionDia
            {
                EmpleadoId = empleadoId,
                Empleado = nombre,
                Fecha = clave.Fecha,
                Ingreso = completas.Count == 0 ? null : completas.Min(j => j.Ingreso),
                Egreso = completas.Count == 0 ? null : completas.Max(j => j.Egreso),
                MinTrabajados = crudo,
                MinAsignados = asignados,
                MinPagables = pagables,
                MinNormales = normales,
                MinExtra = extra,
                MinFeriado = feriado,
                MinExcedente = excedente,
                ExcedenteAprobado = aprobado,
                Ausente = ausente,
                ValorHora = valor,
                RecargoExtraPct = p.RecargoExtraPct,
                RecargoFeriadoPct = p.RecargoFeriadoPct,
                Importe = importe,
                Observacion = observacion.Length == 0 ? null : observacion
            });
        }

        return resultado.OrderBy(d => d.Empleado, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Fecha).ToList();
    }

    public static List<ResumenEmpleado> Resumir(IEnumerable<LiquidacionDia> dias)
    {
        return dias.GroupBy(d => d.Empleado, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResumenEmpleado
            {
                EmpleadoId = g.First().EmpleadoId,
                Empleado = g.First().Empleado,
                MinTrabajados = g.Sum(d => d.MinTrabajados),
                MinAsignados = g.Sum(d => d.MinAsignados),
                MinPagables = g.Sum(d => d.MinPagables),
                MinNormales = g.Sum(d => d.MinNormales),
                MinExtra = g.Sum(d => d.MinExtra),
                MinFeriado = g.Sum(d => d.MinFeriado),
                MinExcedentePendiente = g.Sum(d => d.MinExcedentePendiente),
                Ausencias = g.Count(d => d.Ausente),
                Importe = g.Sum(d => d.Importe)
            })
            .OrderBy(r => r.Empleado, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
