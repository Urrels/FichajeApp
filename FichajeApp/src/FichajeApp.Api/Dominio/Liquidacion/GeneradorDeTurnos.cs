namespace FichajeApp.Api.Dominio.Liquidacion;

// Convierte el horario semanal de cada empleado en turnos de fechas concretas, que es lo que entiende el cálculo.
public static class GeneradorDeTurnos
{
    public static List<TurnoAsignado> Generar(IEnumerable<HorarioEmpleado> horarios, IReadOnlyDictionary<int, string> nombres,
        DateTime desde, DateTime hasta)
    {
        var turnos = new List<TurnoAsignado>();
        var porEmpleado = horarios.Where(h => nombres.ContainsKey(h.EmpleadoId)).ToLookup(h => h.EmpleadoId);

        for (var dia = desde.Date; dia <= hasta.Date; dia = dia.AddDays(1))
        {
            foreach (var grupo in porEmpleado)
            {
                foreach (var h in grupo.Where(x => x.Dia == dia.DayOfWeek))
                {
                    var inicio = dia + h.Entrada;
                    var fin = dia.AddDays(h.TerminaAlDiaSiguiente ? 1 : 0) + h.Salida;
                    turnos.Add(new TurnoAsignado { Empleado = nombres[grupo.Key], Inicio = inicio, Fin = fin });
                }
            }
        }
        return turnos;
    }

    // Valida el horario que cargó el administrador: devuelve el mensaje de error o null si está bien
    public static string? Validar(IReadOnlyCollection<HorarioEmpleado> horarios)
    {
        if (horarios.GroupBy(h => h.Dia).Any(g => g.Count() > 1))
            return "Cada día de la semana puede tener un solo horario.";
        foreach (var h in horarios)
        {
            if (h.Entrada < TimeSpan.Zero || h.Entrada >= TimeSpan.FromHours(24) || h.Salida < TimeSpan.Zero || h.Salida >= TimeSpan.FromHours(24))
                return "Las horas tienen que estar entre 00:00 y 23:59.";
            if (h.Entrada == h.Salida)
                return "La entrada y la salida no pueden ser la misma hora.";
            var minutos = (h.TerminaAlDiaSiguiente ? 24 * 60 : 0) + (h.Salida - h.Entrada).TotalMinutes;
            if (minutos > 16 * 60)
                return "Un turno no puede durar más de 16 horas.";
        }
        return null;
    }
}
