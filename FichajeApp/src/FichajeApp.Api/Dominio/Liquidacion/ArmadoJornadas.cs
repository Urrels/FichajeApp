namespace FichajeApp.Api.Dominio.Liquidacion;

// Convierte los eventos sueltos de la hoja Fichajes (Entrada / Salida) en jornadas.
// En ingenieria_software cada fichaje ya era un par ingreso/egreso; acá hay que reconstruir el par.
public static class ArmadoJornadas
{
    public static List<Jornada> Armar(
        IEnumerable<EventoFichaje> eventos,
        Func<string, int> idDeEmpleado,
        out List<EventoFichaje> huerfanos)
    {
        var jornadas = new List<Jornada>();
        var sueltos = new List<EventoFichaje>();

        foreach (var grupo in eventos.GroupBy(e => e.Empleado, StringComparer.OrdinalIgnoreCase))
        {
            Jornada? abierta = null;

            foreach (var e in grupo.OrderBy(x => x.FechaHora).ThenBy(x => x.Id))
            {
                if (e.Tipo.Equals("Entrada", StringComparison.OrdinalIgnoreCase))
                {
                    // Otra entrada sin haber salido: la anterior queda abierta (incompleta)
                    if (abierta != null) jornadas.Add(abierta);

                    abierta = new Jornada
                    {
                        EmpleadoId = idDeEmpleado(e.Empleado),
                        Empleado = e.Empleado,
                        EntradaId = e.Id,
                        Ingreso = e.FechaHora,
                        Corregido = e.Corregido
                    };
                }
                else if (e.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase))
                {
                    if (abierta == null)
                    {
                        sueltos.Add(e); // salida sin entrada: no se puede pagar, se revisa a mano
                        continue;
                    }

                    abierta.Egreso = e.FechaHora;
                    abierta.SalidaId = e.Id;
                    abierta.Corregido |= e.Corregido;
                    jornadas.Add(abierta);
                    abierta = null;
                }
            }

            if (abierta != null) jornadas.Add(abierta);
        }

        huerfanos = sueltos;
        return jornadas.OrderBy(j => j.Empleado, StringComparer.OrdinalIgnoreCase).ThenBy(j => j.Ingreso).ToList();
    }
}
