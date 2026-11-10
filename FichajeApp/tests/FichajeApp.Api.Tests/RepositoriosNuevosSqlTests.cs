using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Persistencia.Sql;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

// Los repositorios SQL de horarios, notificaciones, dispositivos de push y recorrido, contra una base de verdad
// (SQLite en memoria con las mismas tablas): los tests con repositorios falsos no prueban el mapeo ni las consultas.
public class HorariosSqlTests
{
    private static HorarioEmpleado H(int empleado, DayOfWeek dia, int entrada, int salida) => new()
    {
        EmpleadoId = empleado, Dia = dia, Entrada = TimeSpan.FromHours(entrada), Salida = TimeSpan.FromHours(salida)
    };

    [Fact]
    public async Task GuardarReemplazaElHorarioCompletoDeEsaPersona_SinTocarElDeOtras()
    {
        using var bd = new BaseSqlite();
        var repo = new HorariosSqlRepositorio(bd);

        await repo.GuardarAsync(1, new[] { H(1, DayOfWeek.Monday, 8, 16), H(1, DayOfWeek.Tuesday, 8, 16) });
        await repo.GuardarAsync(2, new[] { H(2, DayOfWeek.Friday, 22, 6) });
        await repo.GuardarAsync(1, new[] { H(1, DayOfWeek.Wednesday, 9, 17) });

        var todos = await repo.ListarAsync();
        Assert.Equal(2, todos.Count);
        Assert.Equal(DayOfWeek.Wednesday, todos.Single(h => h.EmpleadoId == 1).Dia);
        Assert.Equal(TimeSpan.FromHours(22), todos.Single(h => h.EmpleadoId == 2).Entrada);
    }

    [Fact]
    public async Task GuardarUnaListaVacia_BorraElHorario()
    {
        using var bd = new BaseSqlite();
        var repo = new HorariosSqlRepositorio(bd);
        await repo.GuardarAsync(1, new[] { H(1, DayOfWeek.Monday, 8, 16) });

        await repo.GuardarAsync(1, Array.Empty<HorarioEmpleado>());

        Assert.Empty(await repo.ListarAsync());
    }
}

public class NotificacionesSqlTests
{
    private static Notificacion N(int empleado, string titulo, DateTime fecha) => new() { EmpleadoId = empleado, Titulo = titulo, Mensaje = "m", Fecha = fecha };

    [Fact]
    public async Task ListaLasMasNuevasPrimero_ConLimite_YSoloDeEsaPersona()
    {
        using var bd = new BaseSqlite();
        var repo = new NotificacionesSqlRepositorio(bd);
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        await repo.AgregarAsync(N(1, "vieja", t));
        await repo.AgregarAsync(N(1, "nueva", t.AddHours(1)));
        await repo.AgregarAsync(N(2, "de otra persona", t.AddHours(2)));

        var lista = await repo.ListarAsync(1, 10);

        Assert.Equal(new[] { "nueva", "vieja" }, lista.Select(n => n.Titulo));
        Assert.Single(await repo.ListarAsync(1, 1));
    }

    [Fact]
    public async Task ContarYMarcarComoLeidas_PorIdOTodas()
    {
        using var bd = new BaseSqlite();
        var repo = new NotificacionesSqlRepositorio(bd);
        var t = new DateTime(2026, 11, 10, 12, 0, 0);
        await repo.AgregarAsync(N(1, "a", t));
        await repo.AgregarAsync(N(1, "b", t.AddMinutes(1)));
        await repo.AgregarAsync(N(2, "c", t));
        Assert.Equal(2, await repo.ContarNoLeidasAsync(1));

        var primera = (await repo.ListarAsync(1, 10)).Last().Id;
        await repo.MarcarLeidasAsync(1, primera);
        Assert.Equal(1, await repo.ContarNoLeidasAsync(1));

        await repo.MarcarLeidasAsync(1, null);
        Assert.Equal(0, await repo.ContarNoLeidasAsync(1));
        Assert.Equal(1, await repo.ContarNoLeidasAsync(2));   // las de otra persona no se tocan
    }

    [Fact]
    public async Task NoSePuedeMarcarComoLeidaUnaNotificacionAjena()
    {
        using var bd = new BaseSqlite();
        var repo = new NotificacionesSqlRepositorio(bd);
        await repo.AgregarAsync(N(2, "de Beto", new DateTime(2026, 11, 10)));
        var idDeBeto = (await repo.ListarAsync(2, 10)).Single().Id;

        await repo.MarcarLeidasAsync(1, idDeBeto);   // Ana intenta marcarla

        Assert.Equal(1, await repo.ContarNoLeidasAsync(2));
    }
}

public class DispositivosPushSqlTests
{
    private static readonly DateTime Ahora = new(2026, 11, 10, 12, 0, 0);

    [Fact]
    public async Task UnTokenPasaALaCuentaNuevaSiElTelefonoCambiaDeUsuario()
    {
        using var bd = new BaseSqlite();
        var repo = new DispositivosPushSqlRepositorio(bd);

        await repo.GuardarAsync(1, "token-1", Ahora);
        await repo.GuardarAsync(2, "token-1", Ahora.AddMinutes(1));

        Assert.Empty(await repo.ListarTokensAsync(1));
        Assert.Equal(new[] { "token-1" }, await repo.ListarTokensAsync(2));
    }

    [Fact]
    public async Task VariosTelefonosPorPersona_YSeQuitaSoloElIndicado()
    {
        using var bd = new BaseSqlite();
        var repo = new DispositivosPushSqlRepositorio(bd);
        await repo.GuardarAsync(1, "celular", Ahora);
        await repo.GuardarAsync(1, "tablet", Ahora);
        await repo.GuardarAsync(1, "celular", Ahora.AddMinutes(5));   // registrar dos veces no duplica

        Assert.Equal(2, (await repo.ListarTokensAsync(1)).Count);

        await repo.EliminarAsync("celular");

        Assert.Equal(new[] { "tablet" }, await repo.ListarTokensAsync(1));
    }
}

public class RecorridoSqlTests
{
    private static PuntoGuardado P(DateTime fecha, double lat = -34.6, double lon = -58.4) => new() { FechaHora = fecha, Latitud = lat, Longitud = lon, Precision = 15 };

    [Fact]
    public async Task ListaLosPuntosDeUnDiaEnOrden_SoloDeEsaPersona()
    {
        using var bd = new BaseSqlite();
        var repo = new RecorridoSqlRepositorio(bd);
        var dia = new DateTime(2026, 11, 10);
        await repo.AgregarAsync(1, new[] { P(dia.AddHours(10)), P(dia.AddHours(9)), P(dia.AddDays(-1).AddHours(23)) });
        await repo.AgregarAsync(2, new[] { P(dia.AddHours(9)) });

        var puntos = await repo.ListarAsync(1, dia, dia.AddDays(1));

        Assert.Equal(2, puntos.Count);
        Assert.True(puntos[0].FechaHora < puntos[1].FechaHora);
        Assert.Equal(15, puntos[0].Precision);
    }

    [Fact]
    public async Task LaLimpiezaBorraSoloLoAnterior()
    {
        using var bd = new BaseSqlite();
        var repo = new RecorridoSqlRepositorio(bd);
        var corte = new DateTime(2026, 8, 12);
        await repo.AgregarAsync(1, new[] { P(corte.AddDays(-1)), P(corte.AddDays(-30)), P(corte.AddDays(1)) });

        var borrados = await repo.EliminarAnterioresAsync(corte);

        Assert.Equal(2, borrados);
        Assert.Single(await repo.ListarAsync(1, corte.AddYears(-1), corte.AddYears(1)));
    }
}
