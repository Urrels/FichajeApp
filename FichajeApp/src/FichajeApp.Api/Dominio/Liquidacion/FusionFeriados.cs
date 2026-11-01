namespace FichajeApp.Api.Dominio.Liquidacion;

public record ResultadoSincronizacion(int Agregados, int Actualizados, int Retirados, int Omitidos);

public record PlanFeriados(List<Feriado> Agregar, List<Feriado> Actualizar, List<DateTime> Retirar, int Omitidos);

// Regla de fusión entre lo que hay en la planilla y los feriados oficiales. Pura, para probarla sin Google.
//  - Lo cargado a mano o descartado por el administrador nunca se toca.
//  - Lo que vino de la API se actualiza si cambió.
//  - Un feriado futuro que vino de la API y ya no figura en el listado oficial (lo movieron) se retira,
//    pero solo si ese año llegó con un listado razonable: una respuesta vacía o cortada no borra nada.
public static class FusionFeriados
{
    public const int MinimoParaRetirar = 5;

    private static Dictionary<DateTime, Feriado> PorFecha(IEnumerable<Feriado> lista) =>
        lista.GroupBy(f => f.Fecha.Date).ToDictionary(g => g.Key, g => g.Last());

    public static PlanFeriados Planificar(IEnumerable<Feriado> actuales, IReadOnlyCollection<Feriado> oficiales, DateTime hoy)
    {
        var existentes = PorFecha(actuales);
        var unicos = PorFecha(oficiales);
        List<Feriado> agregar = new(), actualizar = new();
        int omitidos = 0;

        foreach (var o in unicos.Values)
        {
            if (!existentes.TryGetValue(o.Fecha.Date, out var a)) agregar.Add(o);
            else if (a.Origen != Feriado.OrigenApi) omitidos++;
            else if (a.Descripcion != o.Descripcion || a.Tipo != o.Tipo) actualizar.Add(o);
        }

        var aniosConfiables = unicos.Values.GroupBy(f => f.Fecha.Year)
            .Where(g => g.Count() >= MinimoParaRetirar).Select(g => g.Key).ToHashSet();
        var retirar = existentes.Values
            .Where(a => a.Origen == Feriado.OrigenApi && a.Fecha.Date >= hoy.Date
                        && aniosConfiables.Contains(a.Fecha.Year) && !unicos.ContainsKey(a.Fecha.Date))
            .Select(a => a.Fecha.Date).ToList();

        return new PlanFeriados(agregar, actualizar, retirar, omitidos);
    }

    public static List<Feriado> Aplicar(IEnumerable<Feriado> actuales, PlanFeriados plan)
    {
        var resultado = PorFecha(actuales);
        foreach (var fecha in plan.Retirar) resultado.Remove(fecha);
        foreach (var f in plan.Actualizar.Concat(plan.Agregar)) resultado[f.Fecha.Date] = f;
        return resultado.Values.OrderBy(f => f.Fecha).ToList();
    }

    public static ResultadoSincronizacion Resumen(PlanFeriados p) =>
        new(p.Agregar.Count, p.Actualizar.Count, p.Retirar.Count, p.Omitidos);
}
