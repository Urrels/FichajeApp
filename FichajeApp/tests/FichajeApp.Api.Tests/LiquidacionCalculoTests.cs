using FichajeApp.Api.Dominio.Liquidacion;

namespace FichajeApp.Api.Tests;

public class LiquidacionCalculoTests
{
    private static readonly DateTime Ahora = new(2026, 10, 31, 12, 0, 0);

    private static Jornada J(string empleado, int id, DateTime ingreso, DateTime? egreso, bool corregida = false) => new()
    {
        Empleado = empleado, EmpleadoId = id, Ingreso = ingreso, Egreso = egreso, Corregido = corregida
    };

    private static EntradaLiquidacion Entrada(params Jornada[] jornadas) => new()
    {
        Jornadas = jornadas.ToList(),
        ValoresHora = { new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1000m, VigenteDesde = new DateTime(2026, 1, 1) } },
        Ahora = Ahora
    };

    private static List<LiquidacionDia> Calcular(EntradaLiquidacion e) =>
        LiquidacionCalculo.Calcular(e, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

    [Fact]
    public void SinTurnos_SePagaLoFichado_ConHorasExtraSobreLaJornadaNormal()
    {
        // miércoles 7/10: 08:00-18:00 = 600 min → 480 normales + 120 extra al 50 %
        var dia = Assert.Single(Calcular(Entrada(J("Ana", 1, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 18, 0, 0)))));

        Assert.Equal(600, dia.MinPagables);
        Assert.Equal(480, dia.MinNormales);
        Assert.Equal(120, dia.MinExtra);
        Assert.Equal(11000m, dia.Importe); // 1000 * (480 + 120 * 1,5) / 60
    }

    [Fact]
    public void Domingo_SePagaConRecargoDeFeriado()
    {
        // domingo 11/10: 4 h con 100 % de recargo
        var dia = Assert.Single(Calcular(Entrada(J("Ana", 1, new(2026, 10, 11, 9, 0, 0), new(2026, 10, 11, 13, 0, 0)))));

        Assert.Equal(240, dia.MinFeriado);
        Assert.Equal(0, dia.MinNormales);
        Assert.Equal(8000m, dia.Importe); // 1000 * (240 * 2) / 60
    }

    [Fact]
    public void FeriadoCargado_SeTrataComoDomingo()
    {
        var e = Entrada(J("Ana", 1, new(2026, 10, 7, 9, 0, 0), new(2026, 10, 7, 13, 0, 0)));
        e.Feriados.Add(new DateTime(2026, 10, 7));

        Assert.Equal(240, Assert.Single(Calcular(e)).MinFeriado);
    }

    [Theory]
    [InlineData(487, 480)] // 8:07 → redondea a 8:00
    [InlineData(488, 495)] // 8:08 → redondea a 8:15
    public void SeRedondeaAlMultiploConfigurado(int minutosFichados, int esperado)
    {
        var inicio = new DateTime(2026, 10, 7, 8, 0, 0);
        var dia = Assert.Single(Calcular(Entrada(J("Ana", 1, inicio, inicio.AddMinutes(minutosFichados)))));
        Assert.Equal(esperado, dia.MinPagables);
    }

    [Fact]
    public void ValorHora_UsaElVigenteEseDia()
    {
        var e = Entrada(
            J("Ana", 1, new(2026, 10, 2, 8, 0, 0), new(2026, 10, 2, 16, 0, 0)),
            J("Ana", 1, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 0, 0)));
        e.ValoresHora.Add(new ValorHoraEmpleado { EmpleadoId = 1, Valor = 1200m, VigenteDesde = new DateTime(2026, 10, 5) });

        var dias = Calcular(e);

        Assert.Equal(8000m, dias[0].Importe);  // 2/10: 8 h a 1000
        Assert.Equal(9600m, dias[1].Importe);  // 7/10: 8 h a 1200
    }

    [Fact]
    public void JornadaIncompleta_NoSePagaYSeMarca()
    {
        var dia = Assert.Single(Calcular(Entrada(J("Ana", 1, new(2026, 10, 7, 8, 0, 0), null))));

        Assert.Equal(0, dia.MinPagables);
        Assert.Equal(0m, dia.Importe);
        Assert.Contains("Fichaje incompleto", dia.Observacion);
    }

    [Fact]
    public void SinValorHora_SeAvisaEnLaObservacion()
    {
        var e = Entrada(J("Beto", 2, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 0, 0)));

        var dia = Assert.Single(Calcular(e));

        Assert.Equal(0m, dia.Importe);
        Assert.Contains("Sin valor hora", dia.Observacion);
    }

    [Fact]
    public void TurnoNocturno_PerteneceAlDiaEnQueEmpezo()
    {
        var dia = Assert.Single(Calcular(Entrada(J("Ana", 1, new(2026, 10, 7, 22, 0, 0), new(2026, 10, 8, 6, 0, 0)))));

        Assert.Equal(new DateTime(2026, 10, 7), dia.Fecha);
        Assert.Equal(480, dia.MinPagables);
    }

    [Fact]
    public void FueraDelPeriodo_NoSeCuenta()
    {
        var e = Entrada(J("Ana", 1, new(2026, 9, 30, 8, 0, 0), new(2026, 9, 30, 16, 0, 0)));
        Assert.Empty(Calcular(e));
    }

    // ---- Con turnos asignados ----

    private static EntradaLiquidacion ConTurno(DateTime fichadoDesde, DateTime fichadoHasta)
    {
        var e = Entrada(J("Ana", 1, fichadoDesde, fichadoHasta));
        e.Turnos.Add(new TurnoAsignado { Empleado = "Ana", Inicio = new(2026, 10, 7, 8, 0, 0), Fin = new(2026, 10, 7, 16, 0, 0) });
        return e;
    }

    [Fact]
    public void ConTurnos_ElExcedenteSinAprobarNoSePaga()
    {
        var dia = Assert.Single(Calcular(ConTurno(new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 17, 0, 0))));

        Assert.Equal(60, dia.MinExcedente);
        Assert.False(dia.ExcedenteAprobado);
        Assert.Equal(480, dia.MinPagables);
        Assert.Contains("Excedente a aprobar", dia.Observacion);
    }

    [Fact]
    public void ConTurnos_ElExcedenteAprobadoSePagaComoExtra()
    {
        var e = ConTurno(new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 17, 0, 0));
        e.ExcedentesAprobados.Add(("ana", new DateTime(2026, 10, 7)));

        var dia = Assert.Single(Calcular(e));

        Assert.True(dia.ExcedenteAprobado);
        Assert.Equal(540, dia.MinPagables);
        Assert.Equal(480, dia.MinNormales);
        Assert.Equal(60, dia.MinExtra);
    }

    [Fact]
    public void ConTurnos_LaToleranciaCuentaComoLoAsignado()
    {
        // 8 min de diferencia ≤ 10 de tolerancia → se paga exactamente lo asignado
        var dia = Assert.Single(Calcular(ConTurno(new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 8, 0))));

        Assert.Equal(480, dia.MinPagables);
        Assert.Equal(0, dia.MinExcedente);
    }

    [Fact]
    public void ConTurnos_SinFichajesEsAusencia()
    {
        var e = Entrada();
        e.Turnos.Add(new TurnoAsignado { Empleado = "Ana", Inicio = new(2026, 10, 7, 8, 0, 0), Fin = new(2026, 10, 7, 16, 0, 0) });

        var dia = Assert.Single(Calcular(e));

        Assert.True(dia.Ausente);
        Assert.Equal(0, dia.MinPagables);
    }

    [Fact]
    public void Resumen_SumaPorEmpleado()
    {
        var e = Entrada(
            J("Ana", 1, new(2026, 10, 6, 8, 0, 0), new(2026, 10, 6, 16, 0, 0)),
            J("Ana", 1, new(2026, 10, 7, 8, 0, 0), new(2026, 10, 7, 16, 0, 0)));

        var resumen = Assert.Single(LiquidacionCalculo.Resumir(Calcular(e)));

        Assert.Equal(960, resumen.MinPagables);
        Assert.Equal(16000m, resumen.Importe);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(95, "1:35")]
    [InlineData(-30, "-0:30")]
    public void Hhmm_FormateaMinutos(int minutos, string esperado) =>
        Assert.Equal(esperado, LiquidacionCalculo.Hhmm(minutos));
}

public class ArmadoJornadasTests
{
    private static EventoFichaje E(int id, string quien, string tipo, DateTime cuando) => new(id, quien, cuando, tipo, false);

    [Fact]
    public void ArmaElParEntradaSalida_AunqueCruceLaMedianoche()
    {
        var jornadas = ArmadoJornadas.Armar(new[]
        {
            E(2, "Ana", "Entrada", new(2026, 10, 7, 22, 0, 0)),
            E(3, "Ana", "Salida", new(2026, 10, 8, 6, 0, 0))
        }, _ => 1, out var huerfanos);

        var j = Assert.Single(jornadas);
        Assert.Empty(huerfanos);
        Assert.Equal(480, j.MinutosTrabajados);
        Assert.Equal(new DateTime(2026, 10, 7), j.Fecha);
        Assert.Equal(2, j.EntradaId);
        Assert.Equal(3, j.SalidaId);
    }

    [Fact]
    public void DosEntradasSeguidas_DejanLaPrimeraAbierta()
    {
        var jornadas = ArmadoJornadas.Armar(new[]
        {
            E(1, "Ana", "Entrada", new(2026, 10, 7, 8, 0, 0)),
            E(2, "Ana", "Entrada", new(2026, 10, 8, 8, 0, 0)),
            E(3, "Ana", "Salida", new(2026, 10, 8, 16, 0, 0))
        }, _ => 1, out _);

        Assert.Equal(2, jornadas.Count);
        Assert.False(jornadas[0].EsCompleta);
        Assert.True(jornadas[1].EsCompleta);
    }

    [Fact]
    public void SalidaSinEntrada_QuedaComoHuerfana()
    {
        var jornadas = ArmadoJornadas.Armar(new[] { E(1, "Ana", "Salida", new(2026, 10, 7, 16, 0, 0)) }, _ => 1, out var huerfanos);

        Assert.Empty(jornadas);
        Assert.Single(huerfanos);
    }

    [Fact]
    public void EmpleadosDistintos_NoSeMezclan_YElOrdenDeLlegadaNoImporta()
    {
        var jornadas = ArmadoJornadas.Armar(new[]
        {
            E(4, "Beto", "Salida", new(2026, 10, 7, 17, 0, 0)),
            E(2, "Ana", "Salida", new(2026, 10, 7, 16, 0, 0)),
            E(1, "Ana", "Entrada", new(2026, 10, 7, 8, 0, 0)),
            E(3, "Beto", "Entrada", new(2026, 10, 7, 9, 0, 0))
        }, n => n == "Ana" ? 1 : 2, out _);

        Assert.Equal(2, jornadas.Count);
        Assert.All(jornadas, j => Assert.True(j.EsCompleta));
        Assert.Equal(480, jornadas.Single(j => j.Empleado == "Ana").MinutosTrabajados);
        Assert.Equal(480, jornadas.Single(j => j.Empleado == "Beto").MinutosTrabajados);
    }

    [Fact]
    public void EntradaAbiertaVencida_SeDetectaPorHoras()
    {
        var j = new Jornada { Ingreso = new DateTime(2026, 10, 7, 8, 0, 0) };

        Assert.True(j.EstaVencida(new DateTime(2026, 10, 8, 1, 0, 0), 16));   // 17 h
        Assert.False(j.EstaVencida(new DateTime(2026, 10, 7, 20, 0, 0), 16)); // 12 h
    }
}
