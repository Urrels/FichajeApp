using Microsoft.EntityFrameworkCore;

namespace FichajeApp.Api.Persistencia.Sql;

// Lo que comparten los repositorios SQL: un DbContext por operación (los repositorios son singletons) y una forma
// de agrupar varios cambios en una transacción que además tolera los cortes de conexión de Azure SQL (reintenta).
public abstract class BaseSql
{
    // Una sola instancia de la API escribe: este lock serializa las secuencias "leer y después escribir" que no caben en una transacción
    protected static readonly SemaphoreSlim Escritura = new(1, 1);

    protected readonly IDbContextFactory<FichajeDbContext> Fabrica;

    protected BaseSql(IDbContextFactory<FichajeDbContext> fabrica) => Fabrica = fabrica;

    protected async Task<T> LeerAsync<T>(Func<FichajeDbContext, Task<T>> consulta)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await consulta(db);
    }

    // Todo o nada. Con reintentos activados EF exige correr la transacción dentro de la estrategia: si se repite,
    // se vuelve a empezar con el contexto limpio.
    protected async Task<T> EnTransaccionAsync<T>(Func<FichajeDbContext, Task<T>> trabajo)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync();
            var resultado = await trabajo(db);
            await transaccion.CommitAsync();
            return resultado;
        });
    }

    protected Task EnTransaccionAsync(Func<FichajeDbContext, Task> trabajo) =>
        EnTransaccionAsync<bool>(async db => { await trabajo(db); return true; });
}
