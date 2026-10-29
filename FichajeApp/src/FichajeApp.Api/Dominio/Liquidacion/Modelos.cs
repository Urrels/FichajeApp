namespace FichajeApp.Api.Dominio.Liquidacion;

public class ParametrosLiquidacion
{
    public int ToleranciaMinutos { get; set; } = 10;
    public int RedondeoMinutos { get; set; } = 15;
    public decimal JornadaNormalHoras { get; set; } = 8;
    public decimal RecargoExtraPct { get; set; } = 50;
    public decimal RecargoFeriadoPct { get; set; } = 100;
    public int MaxHorasFichajeAbierto { get; set; } = 16;
}

public class ValorHoraEmpleado
{
    public int EmpleadoId { get; set; }
    public decimal Valor { get; set; }
    public DateTime VigenteDesde { get; set; }
    public string? CargadoPor { get; set; }
}

public class Feriado
{
    public DateTime Fecha { get; set; }
    public string Descripcion { get; set; } = string.Empty;
}

// Un evento de la hoja Fichajes (Entrada o Salida). Id = número de fila en la hoja.
public record EventoFichaje(int Id, string Empleado, DateTime FechaHora, string Tipo, bool Corregido);

// Un par Entrada/Salida de un empleado. Egreso nulo = entrada abierta (sin salida).
public class Jornada
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public int EntradaId { get; set; }
    public int? SalidaId { get; set; }
    public DateTime Ingreso { get; set; }
    public DateTime? Egreso { get; set; }
    public bool Corregido { get; set; }

    // Un turno nocturno pertenece al día en que empezó
    public DateTime Fecha => Ingreso.Date;
    public bool EsCompleta => Egreso.HasValue;

    public int MinutosTrabajados =>
        Egreso.HasValue ? (int)Math.Round((Egreso.Value - Ingreso).TotalMinutes) : 0;

    public bool EstaVencida(DateTime ahora, int maxHoras) =>
        !Egreso.HasValue && (ahora - Ingreso).TotalHours > maxHoras;
}

// Turno asignado (opcional: FichajeApp hoy no los tiene; si no hay ninguno se paga lo fichado)
public class TurnoAsignado
{
    public string Empleado { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public DateTime Fecha => Inicio.Date;
    public int Minutos => (int)Math.Round((Fin - Inicio).TotalMinutes);
}

public class LiquidacionDia
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? Ingreso { get; set; }
    public DateTime? Egreso { get; set; }
    public int MinTrabajados { get; set; }
    public int MinAsignados { get; set; }
    public int MinPagables { get; set; }
    public int MinNormales { get; set; }
    public int MinExtra { get; set; }
    public int MinFeriado { get; set; }
    public int MinExcedente { get; set; }
    public bool ExcedenteAprobado { get; set; }
    public bool Ausente { get; set; }
    public decimal ValorHora { get; set; }
    public decimal RecargoExtraPct { get; set; }
    public decimal RecargoFeriadoPct { get; set; }
    public decimal Importe { get; set; }
    public string? Observacion { get; set; }

    public int MinExcedentePendiente => ExcedenteAprobado ? 0 : MinExcedente;
}

public class ResumenEmpleado
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public int MinTrabajados { get; set; }
    public int MinAsignados { get; set; }
    public int MinPagables { get; set; }
    public int MinNormales { get; set; }
    public int MinExtra { get; set; }
    public int MinFeriado { get; set; }
    public int MinExcedentePendiente { get; set; }
    public int Ausencias { get; set; }
    public decimal Importe { get; set; }
}

// Un período liquidado: al cerrarlo se guarda una foto de sus días que ya no se recalcula
public class PeriodoLiquidado
{
    public int Id { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public string Estado { get; set; } = "Cerrada"; // Cerrada | Pagada
    public string CerradaPor { get; set; } = string.Empty;
    public DateTime FechaCierre { get; set; }
    public string? PagadaPor { get; set; }
    public DateTime? FechaPago { get; set; }
}

public class EntradaLiquidacion
{
    public List<Jornada> Jornadas { get; set; } = new();
    public List<TurnoAsignado> Turnos { get; set; } = new();
    public List<ValorHoraEmpleado> ValoresHora { get; set; } = new();
    public HashSet<DateTime> Feriados { get; set; } = new();
    public HashSet<(string Empleado, DateTime Fecha)> ExcedentesAprobados { get; set; } = new();
    public ParametrosLiquidacion Parametros { get; set; } = new();
    public DateTime Ahora { get; set; } = DateTime.Now;
}
