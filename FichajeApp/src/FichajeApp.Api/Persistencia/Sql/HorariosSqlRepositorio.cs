using Microsoft.EntityFrameworkCore;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Persistencia.Sql;

// Tabla Horarios: un renglón por empleado y día de la semana.
public class HorariosSqlRepositorio : BaseSql, IHorariosRepositorio
{
    public HorariosSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public Task<List<HorarioEmpleado>> ListarAsync() => LeerAsync(async db =>
        (await db.Horarios.OrderBy(h => h.EmpleadoId).ThenBy(h => h.DiaSemana).ToListAsync())
            .Select(h => new HorarioEmpleado { EmpleadoId = h.EmpleadoId, Dia = (DayOfWeek)h.DiaSemana, Entrada = h.Entrada, Salida = h.Salida })
            .ToList());

    // Con los reintentos de la base activados (Azure SQL) EF no admite transacciones manuales sueltas: se usa EnTransaccionAsync
    public Task GuardarAsync(int empleadoId, IReadOnlyCollection<HorarioEmpleado> horarios) => EnTransaccionAsync(async db =>
    {
        db.Horarios.RemoveRange(await db.Horarios.Where(h => h.EmpleadoId == empleadoId).ToListAsync());
        db.Horarios.AddRange(horarios.Select(h => new HorarioFila
        {
            EmpleadoId = empleadoId, DiaSemana = (int)h.Dia, Entrada = h.Entrada, Salida = h.Salida
        }));
        await db.SaveChangesAsync();
    });
}

public class HorariosNoDisponible : IHorariosRepositorio
{
    public Task<List<HorarioEmpleado>> ListarAsync() => Task.FromResult(new List<HorarioEmpleado>());

    public Task GuardarAsync(int empleadoId, IReadOnlyCollection<HorarioEmpleado> horarios) =>
        throw new InvalidOperationException("Los horarios por empleado necesitan la base de datos SQL.");
}
