using FichajeApp.Api.Dominio.Liquidacion;

namespace FichajeApp.Api.Tests;

public class HorariosTests
{
    private static HorarioEmpleado H(int empleado, DayOfWeek dia, int entrada, int salida) => new()
    {
        EmpleadoId = empleado, Dia = dia, Entrada = TimeSpan.FromHours(entrada), Salida = TimeSpan.FromHours(salida)
    };

    private static readonly Dictionary<int, string> Nombres = new() { [1] = "Ana", [2] = "Beto" };

    [Fact]
    public void Generar_ArmaUnTurnoPorCadaDiaConHorarioDelRango()
    {
        var horarios = new[] { H(1, DayOfWeek.Monday, 8, 16), H(1, DayOfWeek.Wednesday, 8, 16) };

        // 5 al 11 de octubre de 2026: lunes 5 a domingo 11
        var turnos = GeneradorDeTurnos.Generar(horarios, Nombres, new(2026, 10, 5), new(2026, 10, 11));

        Assert.Equal(2, turnos.Count);
        Assert.Equal(new DateTime(2026, 10, 5, 8, 0, 0), turnos[0].Inicio);
        Assert.Equal(new DateTime(2026, 10, 7, 16, 0, 0), turnos[1].Fin);
        Assert.All(turnos, t => Assert.Equal("Ana", t.Empleado));
    }

    [Fact]
    public void Generar_TurnoNocturnoTerminaAlDiaSiguiente()
    {
        var turno = Assert.Single(GeneradorDeTurnos.Generar(new[] { H(1, DayOfWeek.Friday, 22, 6) }, Nombres, new(2026, 10, 9), new(2026, 10, 9)));

        Assert.Equal(new DateTime(2026, 10, 9, 22, 0, 0), turno.Inicio);
        Assert.Equal(new DateTime(2026, 10, 10, 6, 0, 0), turno.Fin);
        Assert.Equal(480, turno.Minutos);
    }

    [Fact]
    public void Generar_IgnoraEmpleadosQueYaNoExisten()
    {
        Assert.Empty(GeneradorDeTurnos.Generar(new[] { H(99, DayOfWeek.Monday, 8, 16) }, Nombres, new(2026, 10, 5), new(2026, 10, 5)));
    }

    [Fact]
    public void Validar_AceptaUnHorarioNormal()
    {
        Assert.Null(GeneradorDeTurnos.Validar(new[] { H(1, DayOfWeek.Monday, 8, 16), H(1, DayOfWeek.Tuesday, 22, 6) }));
    }

    [Fact]
    public void Validar_RechazaDiaRepetido_MismaHoraYTurnosMuyLargos()
    {
        Assert.NotNull(GeneradorDeTurnos.Validar(new[] { H(1, DayOfWeek.Monday, 8, 12), H(1, DayOfWeek.Monday, 14, 18) }));
        Assert.NotNull(GeneradorDeTurnos.Validar(new[] { H(1, DayOfWeek.Monday, 8, 8) }));
        Assert.NotNull(GeneradorDeTurnos.Validar(new[] { H(1, DayOfWeek.Monday, 6, 23) }));   // 17 h
    }

    // ---- Cálculo: el modo por turnos vale por empleado ----

    private static Jornada J(string empleado, int id, DateTime ingreso, DateTime egreso) => new()
    {
        Empleado = empleado, EmpleadoId = id, Ingreso = ingreso, Egreso = egreso
    };

    private static List<LiquidacionDia> Calcular(EntradaLiquidacion e) =>
        LiquidacionCalculo.Calcular(e, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

    [Fact]
    public void QuienNoTieneHorarioSeSigueCobrandoLoFichado_AunqueOtroLoTenga()
    {
        var e = new EntradaLiquidacion
        {
            Jornadas =
            {
                J("Ana", 1, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 0, 0)),
                J("Beto", 2, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 0, 0))
            },
            ValoresHora =
            {
                new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) },
                new ValorHoraEmpleado { EmpleadoId = 2, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) }
            },
            Turnos = { new TurnoAsignado { Empleado = "Ana", Inicio = new(2026, 10, 7, 8, 0, 0), Fin = new(2026, 10, 7, 16, 0, 0) } },
            Ahora = new DateTime(2026, 10, 31, 12, 0, 0)
        };

        var dias = Calcular(e);

        Assert.Equal(480, dias.Single(d => d.Empleado == "Ana").MinPagables);
        var beto = dias.Single(d => d.Empleado == "Beto");
        Assert.Equal(480, beto.MinPagables);   // antes quedaba todo como excedente sin pagar
        Assert.Equal(0, beto.MinExcedente);
    }

    [Fact]
    public void ConHorario_UnDiaSinTurnoEsExcedenteAAprobar()
    {
        var e = new EntradaLiquidacion
        {
            // trabaja un sábado y su horario es de lunes a viernes
            Jornadas = { J("Ana", 1, new(2026, 10, 10, 8, 0, 0), new(2026, 10, 10, 12, 0, 0)) },
            ValoresHora = { new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) } },
            EmpleadosConHorario = { "ana" },
            Ahora = new DateTime(2026, 10, 31, 12, 0, 0)
        };

        var dia = Assert.Single(Calcular(e));

        Assert.Equal(240, dia.MinExcedente);
        Assert.Equal(0, dia.MinPagables);
        Assert.Contains("Sin turno asignado", dia.Observacion);
    }
}
