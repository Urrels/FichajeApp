using FichajeApp.Api.Models;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Enrutamiento de avisos según la jerarquía. Cuando un empleado hace algo que su superior tiene que saber
// (ficha, deja un fichaje abierto, falla la huella o la cara):
//   1) Su SUPERVISOR directo recibe el aviso en la app y por push, para actuar enseguida.
//   2) Los administradores reciben un registro en su centro de notificaciones, SIN push, para auditar cuando quieran.
//   3) Si no tiene un supervisor que hoy pueda actuar (nunca se le asignó, se dio de baja, perdió el permiso o está
//      bloqueado), el aviso va directo a los administradores CON push, aclarando que no tiene supervisor.
public class AvisosPorJerarquia
{
    private readonly JerarquiaService _jerarquia;
    private readonly NotificacionesService _notificaciones;

    public AvisosPorJerarquia(JerarquiaService jerarquia, NotificacionesService notificaciones)
    {
        _jerarquia = jerarquia;
        _notificaciones = notificaciones;
    }

    // Devuelve a quiénes se les avisó con push (para las pruebas y el registro)
    public async Task<IReadOnlyList<int>> AvisarAsync(EmpleadoDto autor, string titulo, string mensaje)
    {
        var d = await _jerarquia.DestinatariosDeAsync(autor);
        var conPush = new List<int>();

        if (d.Supervisor is not null)
        {
            await _notificaciones.CrearAsync(d.Supervisor.Id, titulo, mensaje, push: true);
            conPush.Add(d.Supervisor.Id);

            // Registro para auditoría: sin sonar, y sin repetirle a un administrador que ya recibió el aviso como supervisor
            foreach (var admin in d.Administradores.Where(a => a.Id != d.Supervisor.Id))
                await _notificaciones.CrearAsync(admin.Id, titulo, mensaje, push: false);
        }
        else
        {
            var conAclaracion = mensaje + " (Sin supervisor asignado.)";
            foreach (var admin in d.Administradores)
            {
                await _notificaciones.CrearAsync(admin.Id, titulo, conAclaracion, push: true);
                conPush.Add(admin.Id);
            }
        }
        return conPush;
    }
}

// Cada entrada o salida de un empleado avisa a quien corresponda según la jerarquía
public class AvisoFichajeASupervisores : IFichajeObserver
{
    private readonly IEmpleadosRepositorio _empleados;
    private readonly AvisosPorJerarquia _avisos;
    private readonly ILogger<AvisoFichajeASupervisores> _log;

    public AvisoFichajeASupervisores(IEmpleadosRepositorio empleados, AvisosPorJerarquia avisos, ILogger<AvisoFichajeASupervisores> log)
    {
        _empleados = empleados;
        _avisos = avisos;
        _log = log;
    }

    // El fichaje ya está guardado: el aviso corre aparte para no demorar ni romper el fichaje
    public void OnFichajeRegistrado(FichajeDto fichaje) =>
        _ = Task.Run(async () =>
        {
            try { await AvisarAsync(fichaje); }
            catch (Exception ex) { _log.LogWarning("No se pudo avisar del fichaje: {Tipo}", ex.GetType().Name); }
        });

    public async Task AvisarAsync(FichajeDto fichaje)
    {
        var autor = (await _empleados.ListarAsync())
            .FirstOrDefault(e => e.NombreCompleto.Equals(fichaje.NombreCompleto, StringComparison.OrdinalIgnoreCase));
        if (autor is null) return;

        var (titulo, mensaje) = Textos(fichaje);
        await _avisos.AvisarAsync(autor, titulo, mensaje);
    }

    public static (string Titulo, string Mensaje) Textos(FichajeDto f)
    {
        bool entrada = f.TipoFichaje.Equals("Entrada", StringComparison.OrdinalIgnoreCase);
        var hora = f.FechaHora.ToString("HH:mm");
        var lugar = string.IsNullOrWhiteSpace(f.Lugar) || f.Lugar == "Sin ubicación" ? "" : $" ({f.Lugar})";
        return (entrada ? "Entrada registrada" : "Salida registrada",
                $"{f.NombreCompleto} fichó {(entrada ? "la entrada" : "la salida")} a las {hora}{lugar}.");
    }
}
