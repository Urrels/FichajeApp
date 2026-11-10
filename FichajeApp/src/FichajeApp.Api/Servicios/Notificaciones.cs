using FichajeApp.Api.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace FichajeApp.Api.Servicios;

public class Notificacion
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public DateTime Fecha { get; set; }
    public bool Leida { get; set; }
}

public interface INotificacionesRepositorio
{
    Task AgregarAsync(Notificacion notificacion);
    Task<List<Notificacion>> ListarAsync(int empleadoId, int maximo);
    Task<int> ContarNoLeidasAsync(int empleadoId);
    // id null = todas las de esa persona
    Task MarcarLeidasAsync(int empleadoId, int? id);
}

public class NotificacionesSqlRepositorio : Persistencia.Sql.BaseSql, INotificacionesRepositorio
{
    public NotificacionesSqlRepositorio(IDbContextFactory<FichajeDbContext> fabrica) : base(fabrica) { }

    public async Task AgregarAsync(Notificacion n)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        db.Notificaciones.Add(new NotificacionFila
        {
            EmpleadoId = n.EmpleadoId, Titulo = n.Titulo, Mensaje = n.Mensaje, Fecha = n.Fecha, Leida = false
        });
        await db.SaveChangesAsync();
    }

    public Task<List<Notificacion>> ListarAsync(int empleadoId, int maximo) => LeerAsync(async db =>
        (await db.Notificaciones.Where(x => x.EmpleadoId == empleadoId).OrderByDescending(x => x.Fecha).Take(maximo).ToListAsync())
            .Select(x => new Notificacion
            {
                Id = x.Id, EmpleadoId = x.EmpleadoId, Titulo = x.Titulo, Mensaje = x.Mensaje, Fecha = x.Fecha, Leida = x.Leida
            }).ToList());

    public Task<int> ContarNoLeidasAsync(int empleadoId) =>
        LeerAsync(db => db.Notificaciones.CountAsync(x => x.EmpleadoId == empleadoId && !x.Leida));

    public async Task MarcarLeidasAsync(int empleadoId, int? id)
    {
        await using var db = await Fabrica.CreateDbContextAsync();
        await db.Notificaciones.Where(x => x.EmpleadoId == empleadoId && !x.Leida && (id == null || x.Id == id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Leida, true));
    }
}

// Con la planilla de Google no hay dónde guardarlas: la app funciona igual, sin centro de notificaciones
public class NotificacionesNoDisponible : INotificacionesRepositorio
{
    public Task AgregarAsync(Notificacion notificacion) => Task.CompletedTask;
    public Task<List<Notificacion>> ListarAsync(int empleadoId, int maximo) => Task.FromResult(new List<Notificacion>());
    public Task<int> ContarNoLeidasAsync(int empleadoId) => Task.FromResult(0);
    public Task MarcarLeidasAsync(int empleadoId, int? id) => Task.CompletedTask;
}

// Avisos dentro de la app (centro de notificaciones). Crear uno nunca rompe la operación que lo origina.
public class NotificacionesService
{
    public const int MaximoPorPersona = 50;

    private readonly INotificacionesRepositorio _repo;
    private readonly TimeProvider _tiempo;
    private readonly ILogger<NotificacionesService>? _log;

    public NotificacionesService(INotificacionesRepositorio repo, TimeProvider tiempo, ILogger<NotificacionesService>? log = null)
    {
        _repo = repo;
        _tiempo = tiempo;
        _log = log;
    }

    public async Task CrearAsync(int empleadoId, string titulo, string mensaje)
    {
        try
        {
            await _repo.AgregarAsync(new Notificacion
            {
                EmpleadoId = empleadoId,
                Titulo = Recortar(titulo, 120),
                Mensaje = Recortar(mensaje, 400),
                Fecha = _tiempo.GetLocalNow().DateTime
            });
        }
        catch (Exception ex) { _log?.LogWarning("No se pudo guardar una notificación: {Tipo}", ex.GetType().Name); }
    }

    public Task<List<Notificacion>> ListarAsync(int empleadoId) => _repo.ListarAsync(empleadoId, MaximoPorPersona);
    public Task<int> ContarNoLeidasAsync(int empleadoId) => _repo.ContarNoLeidasAsync(empleadoId);
    public Task MarcarLeidasAsync(int empleadoId, int? id = null) => _repo.MarcarLeidasAsync(empleadoId, id);

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
