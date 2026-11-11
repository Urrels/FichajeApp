using Microsoft.EntityFrameworkCore;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Persistencia.Sql;

// Tablas ParametrosLiquidacion, ValorHora, Feriados, ExcedentesAprobados, Liquidaciones y LiquidacionDias.
// Cerrar una liquidación es atómico: el período y sus días se guardan juntos o no se guarda nada.
public class LiquidacionSqlRepositorio : BaseSql, ILiquidacionRepositorio
{
    public LiquidacionSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    // ---- Parámetros ----

    public Task<ParametrosLiquidacion> ObtenerParametrosAsync() => LeerAsync(async db =>
    {
        var p = await db.Parametros.FirstOrDefaultAsync(x => x.Id == 1) ?? new ParametrosFila();
        return new ParametrosLiquidacion
        {
            ToleranciaMinutos = p.ToleranciaMinutos, RedondeoMinutos = p.RedondeoMinutos, JornadaNormalHoras = p.JornadaNormalHoras,
            RecargoExtraPct = p.RecargoExtraPct, RecargoFeriadoPct = p.RecargoFeriadoPct,
            MaxHorasFichajeAbierto = p.MaxHorasFichajeAbierto, PagarPuentes = p.PagarPuentes
        };
    });

    public async Task GuardarParametrosAsync(ParametrosLiquidacion p)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var f = await db.Parametros.FirstOrDefaultAsync(x => x.Id == 1);
        if (f is null) db.Parametros.Add(f = new ParametrosFila { Id = 1 });
        f.ToleranciaMinutos = p.ToleranciaMinutos;
        f.RedondeoMinutos = p.RedondeoMinutos;
        f.JornadaNormalHoras = p.JornadaNormalHoras;
        f.RecargoExtraPct = p.RecargoExtraPct;
        f.RecargoFeriadoPct = p.RecargoFeriadoPct;
        f.MaxHorasFichajeAbierto = p.MaxHorasFichajeAbierto;
        f.PagarPuentes = p.PagarPuentes;
        await db.SaveChangesAsync();
    }

    // ---- Valor hora ----

    public Task<List<ValorHoraEmpleado>> ListarValoresHoraAsync() => LeerAsync(async db =>
        (await db.ValoresHora.OrderBy(v => v.EmpleadoId).ThenBy(v => v.VigenteDesde).ToListAsync())
            .Select(v => new ValorHoraEmpleado
            {
                EmpleadoId = v.EmpleadoId, Valor = v.Valor, VigenteDesde = v.VigenteDesde, CargadoPor = v.CargadoPor
            }).ToList());

    // Un empleado solo puede tener un valor por fecha de vigencia
    public async Task GuardarValorHoraAsync(ValorHoraEmpleado v)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var desde = v.VigenteDesde.Date;
        var fila = await db.ValoresHora.FirstOrDefaultAsync(x => x.EmpleadoId == v.EmpleadoId && x.VigenteDesde == desde);
        if (fila is null) db.ValoresHora.Add(fila = new ValorHoraFila { EmpleadoId = v.EmpleadoId, VigenteDesde = desde });
        fila.Valor = v.Valor;
        fila.CargadoPor = v.CargadoPor;
        await db.SaveChangesAsync();
    }

    // ---- Feriados ----

    private static Feriado Mapear(FeriadoFila f) => new()
    {
        Fecha = f.Fecha.Date,
        Descripcion = f.Descripcion,
        Tipo = f.Tipo,
        Origen = f.Origen switch
        {
            Feriado.OrigenApi => Feriado.OrigenApi,
            Feriado.OrigenExcluido => Feriado.OrigenExcluido,
            _ => Feriado.OrigenManual
        }
    };

    public Task<List<Feriado>> ListarFeriadosAsync() => LeerAsync(async db =>
        (await db.Feriados.OrderBy(f => f.Fecha).ToListAsync()).Select(Mapear).ToList());

    // Se lee y se escribe dentro de la misma transacción y del mismo lock: un feriado que alguien cargue a mano mientras tanto no se pisa
    public async Task<ResultadoSincronizacion> AplicarOficialesAsync(IReadOnlyCollection<Feriado> oficiales, DateTime hoy)
    {
        await Escritura.WaitAsync();
        try
        {
            return await EnTransaccionAsync(async db =>
            {
                var actuales = (await db.Feriados.OrderBy(f => f.Fecha).ToListAsync()).Select(Mapear).ToList();
                var plan = FusionFeriados.Planificar(actuales, oficiales, hoy);
                if (plan.Actualizar.Count + plan.Retirar.Count > 0)
                {
                    var resultado = FusionFeriados.Aplicar(actuales, plan);
                    await db.Feriados.ExecuteDeleteAsync();
                    db.ChangeTracker.Clear();
                    db.Feriados.AddRange(resultado.Select(f => new FeriadoFila
                        { Fecha = f.Fecha.Date, Descripcion = f.Descripcion, Tipo = f.Tipo, Origen = f.Origen }));
                }
                else
                {
                    db.Feriados.AddRange(plan.Agregar.Select(f => new FeriadoFila
                        { Fecha = f.Fecha.Date, Descripcion = f.Descripcion, Tipo = f.Tipo, Origen = f.Origen }));
                }
                await db.SaveChangesAsync();
                return FusionFeriados.Resumen(plan);
            });
        }
        finally { Escritura.Release(); }
    }

    public async Task GuardarFeriadoAsync(Feriado feriado)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fecha = feriado.Fecha.Date;
        var fila = await db.Feriados.FirstOrDefaultAsync(f => f.Fecha == fecha);
        if (fila is null) db.Feriados.Add(fila = new FeriadoFila { Fecha = fecha });
        fila.Descripcion = feriado.Descripcion;
        fila.Tipo = feriado.Tipo;
        fila.Origen = feriado.Origen;
        await db.SaveChangesAsync();
    }

    public async Task EliminarFeriadoAsync(DateTime fecha)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var dia = fecha.Date;
        await db.Feriados.Where(f => f.Fecha == dia).ExecuteDeleteAsync();
    }

    // ---- Excedentes aprobados ----

    public Task<List<(string Empleado, DateTime Fecha)>> ListarExcedentesAprobadosAsync() => LeerAsync(async db =>
        (await db.Excedentes.OrderBy(e => e.Id).ToListAsync()).Select(e => (e.Empleado, e.Fecha.Date)).ToList());

    public async Task AprobarExcedenteAsync(string empleado, DateTime fecha, string aprobadoPor)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var dia = fecha.Date;
        var nombre = empleado.ToLower();
        if (await db.Excedentes.AnyAsync(e => e.Empleado.ToLower() == nombre && e.Fecha == dia)) return;
        db.Excedentes.Add(new ExcedenteFila { Empleado = empleado, Fecha = dia, AprobadoPor = aprobadoPor });
        await db.SaveChangesAsync();
    }

    public async Task RevocarExcedenteAsync(string empleado, DateTime fecha)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var dia = fecha.Date;
        var nombre = empleado.ToLower();
        await db.Excedentes.Where(e => e.Empleado.ToLower() == nombre && e.Fecha == dia).ExecuteDeleteAsync();
    }

    // ---- Liquidaciones ----

    public Task<List<PeriodoLiquidado>> ListarLiquidacionesAsync() => LeerAsync(async db =>
        (await db.Liquidaciones.OrderByDescending(l => l.Desde).ToListAsync()).Select(Mapear).ToList());

    public Task<bool> HayLiquidacionEnRangoAsync(DateTime desde, DateTime hasta)
    {
        var d = desde.Date;
        var h = hasta.Date;
        return LeerAsync(db => db.Liquidaciones.AnyAsync(l => l.Desde <= h && l.Hasta >= d));
    }

    public async Task<int> GuardarLiquidacionAsync(PeriodoLiquidado p, IReadOnlyCollection<LiquidacionDia> dias)
    {
        await Escritura.WaitAsync();
        try
        {
            return await EnTransaccionAsync(async db =>
            {
                var desde = p.Desde.Date;
                var hasta = p.Hasta.Date;
                if (await db.Liquidaciones.AnyAsync(l => l.Desde <= hasta && l.Hasta >= desde))
                    throw new InvalidOperationException("Ya existe una liquidación que se superpone con ese período.");

                var fila = new LiquidacionFila
                {
                    Desde = desde, Hasta = hasta, Estado = p.Estado, CerradaPor = p.CerradaPor, FechaCierre = p.FechaCierre,
                    Dias = dias.Select(d => new LiquidacionDiaFila
                    {
                        EmpleadoId = d.EmpleadoId, Empleado = d.Empleado, Fecha = d.Fecha.Date, Ingreso = d.Ingreso, Egreso = d.Egreso,
                        MinTrabajados = d.MinTrabajados, MinAsignados = d.MinAsignados, MinPagables = d.MinPagables,
                        MinNormales = d.MinNormales, MinExtra = d.MinExtra, MinFeriado = d.MinFeriado, MinExcedente = d.MinExcedente,
                        ExcedenteAprobado = d.ExcedenteAprobado, Ausente = d.Ausente, ValorHora = d.ValorHora,
                        RecargoExtraPct = d.RecargoExtraPct, RecargoFeriadoPct = d.RecargoFeriadoPct, Importe = d.Importe,
                        Observacion = string.IsNullOrWhiteSpace(d.Observacion) ? null : d.Observacion
                    }).ToList()
                };
                db.Liquidaciones.Add(fila);
                await db.SaveChangesAsync();
                p.Id = fila.Id;
                return fila.Id;
            });
        }
        finally { Escritura.Release(); }
    }

    public async Task MarcarPagadaAsync(int liquidacionId, string pagadaPor, DateTime fechaPago)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var fila = await db.Liquidaciones.FirstOrDefaultAsync(l => l.Id == liquidacionId)
            ?? throw new InvalidOperationException("La liquidación no existe.");
        fila.Estado = "Pagada";
        fila.PagadaPor = pagadaPor;
        fila.FechaPago = fechaPago;
        await db.SaveChangesAsync();
    }

    public Task<List<LiquidacionDia>> ListarDiasDeLiquidacionAsync(int liquidacionId) => LeerAsync(async db =>
        (await db.LiquidacionDias.Where(d => d.LiquidacionId == liquidacionId).ToListAsync())
            .OrderBy(d => d.Empleado, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Fecha)
            .Select(d => new LiquidacionDia
            {
                EmpleadoId = d.EmpleadoId, Empleado = d.Empleado, Fecha = d.Fecha.Date, Ingreso = d.Ingreso, Egreso = d.Egreso,
                MinTrabajados = d.MinTrabajados, MinAsignados = d.MinAsignados, MinPagables = d.MinPagables,
                MinNormales = d.MinNormales, MinExtra = d.MinExtra, MinFeriado = d.MinFeriado, MinExcedente = d.MinExcedente,
                ExcedenteAprobado = d.ExcedenteAprobado, Ausente = d.Ausente, ValorHora = d.ValorHora,
                RecargoExtraPct = d.RecargoExtraPct, RecargoFeriadoPct = d.RecargoFeriadoPct, Importe = d.Importe,
                Observacion = d.Observacion
            }).ToList());

    private static PeriodoLiquidado Mapear(LiquidacionFila l) => new()
    {
        Id = l.Id, Desde = l.Desde.Date, Hasta = l.Hasta.Date, Estado = l.Estado, CerradaPor = l.CerradaPor,
        FechaCierre = l.FechaCierre, PagadaPor = string.IsNullOrWhiteSpace(l.PagadaPor) ? null : l.PagadaPor, FechaPago = l.FechaPago
    };
}
