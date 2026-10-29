using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

public class UsuariosViewModel : VmBase
{
    private readonly AdminApiService _api;
    private string _busqueda = string.Empty;

    public UsuariosViewModel(AdminApiService api) => _api = api;

    public ObservableCollection<UsuarioAdmin> Usuarios { get; } = new();

    public string Busqueda
    {
        get => _busqueda;
        set => Set(ref _busqueda, value);
    }

    public Task CargarAsync() => ConEspera(async () =>
    {
        Estado = "Cargando...";
        var r = await _api.ListarUsuarios(Busqueda);

        Usuarios.Clear();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        foreach (var u in r.Datos.Items) Usuarios.Add(u);
        Estado = r.Datos.Total == 0 ? "No hay usuarios" : $"{r.Datos.Total} usuario(s)";
    });

    public async Task NuevoAsync()
    {
        var nombre = await Dialogos.Texto("Nuevo usuario", "Nombre completo (es único: los fichajes se identifican por nombre)", maximo: 80);
        if (string.IsNullOrEmpty(nombre)) return;
        var email = await Dialogos.Texto("Nuevo usuario", "Email", maximo: 100, teclado: Keyboard.Email);
        if (string.IsNullOrEmpty(email)) return;
        var pin = await Dialogos.Texto("Nuevo usuario", "PIN de 4 dígitos", maximo: 4, teclado: Keyboard.Numeric);
        if (string.IsNullOrEmpty(pin)) return;

        var roles = await ObtenerRolesAsync();
        if (roles is null) return;
        var porDefecto = roles.Where(r => r.Nombre == "Empleado").Select(r => r.Id);
        var elegidos = await Dialogos.ElegirVarios("Roles", roles.Select(r => (r.Id, r.Nombre)).ToList(), porDefecto);
        if (elegidos is null) return;

        await Ejecutar(() => _api.CrearUsuario(nombre, email, pin, elegidos), "Usuario creado");
    }

    public async Task AccionesAsync(UsuarioAdmin usuario)
    {
        var opciones = new List<string> { "Editar datos", "Asignar roles", "Restablecer PIN" };
        if (usuario.Bloqueado) opciones.Add("Desbloquear");
        opciones.Add("Ver historial");

        var elegida = await Dialogos.Elegir(usuario.NombreCompleto, opciones, usuario.Activo ? "Dar de baja" : "Reactivar");
        switch (elegida)
        {
            case "Editar datos": await EditarAsync(usuario); break;
            case "Asignar roles": await AsignarRolesAsync(usuario); break;
            case "Restablecer PIN": await RestablecerPinAsync(usuario); break;
            case "Desbloquear": await Ejecutar(() => _api.Desbloquear(usuario.Id), "Usuario desbloqueado"); break;
            case "Ver historial": await HistorialAsync(usuario); break;
            case "Dar de baja":
                if (await Dialogos.Confirmar("Dar de baja", $"{usuario.NombreCompleto} no podrá volver a entrar. Sus fichajes se conservan.", "Dar de baja"))
                    await Ejecutar(() => _api.DarDeBaja(usuario.Id), "Usuario dado de baja");
                break;
            case "Reactivar": await Ejecutar(() => _api.Reactivar(usuario.Id), "Usuario reactivado"); break;
        }
    }

    private async Task EditarAsync(UsuarioAdmin u)
    {
        var nombre = await Dialogos.Texto("Editar datos", "Nombre completo (no se puede cambiar si ya tiene fichajes)", u.NombreCompleto, 80);
        if (string.IsNullOrEmpty(nombre)) return;
        var email = await Dialogos.Texto("Editar datos", "Email", u.Email, 100, Keyboard.Email);
        if (string.IsNullOrEmpty(email)) return;

        await Ejecutar(() => _api.EditarUsuario(u.Id, nombre, email), "Datos actualizados");
    }

    private async Task AsignarRolesAsync(UsuarioAdmin u)
    {
        var roles = await ObtenerRolesAsync();
        if (roles is null) return;

        var elegidos = await Dialogos.ElegirVarios($"Roles de {u.NombreCompleto}", roles.Select(r => (r.Id, r.Nombre)).ToList(), u.RolIds);
        if (elegidos is null) return;

        await Ejecutar(() => _api.AsignarRoles(u.Id, elegidos), "Roles asignados");
    }

    private async Task RestablecerPinAsync(UsuarioAdmin u)
    {
        var pin = await Dialogos.Texto("Restablecer PIN", $"Nuevo PIN de 4 dígitos para {u.NombreCompleto}", maximo: 4, teclado: Keyboard.Numeric);
        if (string.IsNullOrEmpty(pin)) return;

        await Ejecutar(() => _api.RestablecerPin(u.Id, pin), "PIN restablecido");
    }

    private async Task HistorialAsync(UsuarioAdmin u)
    {
        var r = await _api.HistorialDeUsuario(u.Id);
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
        if (r.Datos.Count == 0) { await Dialogos.Aviso("Historial", "Este usuario todavía no tiene cambios registrados."); return; }

        var lineas = r.Datos.Take(30).ToDictionary(h => $"{h.FechaHora:dd/MM HH:mm} · {h.Tipo} · {h.RealizadoPor}", h => h);
        var elegida = await Dialogos.Elegir("Historial (tocá uno para volver a ese estado)", lineas.Keys);
        if (elegida is null) return;

        var version = lineas[elegida];
        if (await Dialogos.Confirmar("Restaurar", $"¿Volver al estado de {elegida}? Se restauran email, estado, bloqueo y roles (no el PIN).", "Restaurar"))
            await Ejecutar(() => _api.Rollback(version.Id), "Usuario restaurado");
    }

    private async Task<List<RolResumen>?> ObtenerRolesAsync()
    {
        var r = await _api.ListarRolesParaAsignar();
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return null; }
        return r.Datos;
    }

    private async Task Ejecutar(Func<Task<ResultadoConsulta<Respuesta>>> accion, string exito)
    {
        await ConEspera(async () =>
        {
            var r = await accion();
            if (!r.Exito) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
            Estado = exito;
        });
        await CargarAsync();
    }
}
