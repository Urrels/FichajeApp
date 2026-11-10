using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

public class UsuariosViewModel : VmBase
{
    private readonly AdminApiService _api;
    private readonly PerfilFotos _perfiles;
    private string _busqueda = string.Empty;

    public UsuariosViewModel(AdminApiService api, PerfilFotos perfiles)
    {
        _api = api;
        _perfiles = perfiles;
    }

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

    // Pantalla propia con vista previa; al volver, UsuariosPage.OnAppearing recarga la lista
    public Task NuevoAsync() => Shell.Current.GoToAsync(nameof(Views.UsuarioNuevoPage));

    public async Task AccionesAsync(UsuarioAdmin usuario)
    {
        var opciones = new List<string> { "Editar datos", "Foto de perfil", "Asignar roles", "Horario semanal", "Restablecer PIN" };
        if (usuario.Bloqueado) opciones.Add("Desbloquear");
        opciones.Add("Ver historial");

        var elegida = await Dialogos.Elegir(usuario.NombreCompleto, opciones, usuario.Activo ? "Dar de baja" : "Reactivar");
        switch (elegida)
        {
            case "Editar datos": await EditarAsync(usuario); break;
            case "Foto de perfil": await FotoDePerfilAsync(usuario); break;
            case "Asignar roles": await AsignarRolesAsync(usuario); break;
            case "Horario semanal":
                await Shell.Current.GoToAsync($"{nameof(Views.HorarioPage)}?id={usuario.Id}&nombre={Uri.EscapeDataString(usuario.NombreCompleto)}");
                break;
            case "Restablecer PIN": await RestablecerPinAsync(usuario); break;
            case "Desbloquear": await Ejecutar(() => _api.Desbloquear(usuario.Id), "Usuario desbloqueado"); break;
            case "Ver historial": await HistorialAsync(usuario); break;
            case "Dar de baja":
                if (await Dialogos.Confirmar($"¿Dar de baja a {usuario.NombreCompleto}?", "Ya no va a poder entrar a la app. Sus fichajes se conservan y podés reactivarlo cuando quieras.", "Dar de baja"))
                    await Ejecutar(() => _api.DarDeBaja(usuario.Id), "Usuario dado de baja");
                break;
            case "Reactivar": await Ejecutar(() => _api.Reactivar(usuario.Id), "Usuario reactivado"); break;
        }
    }

    private async Task FotoDePerfilAsync(UsuarioAdmin u)
    {
        const string Sacar = "Sacar una foto", Quitar = "Quitar la foto";
        var elegida = await Dialogos.Elegir($"Foto de {u.NombreCompleto}", new[] { Sacar, Quitar });
        if (elegida is null) return;

        if (elegida == Quitar)
        {
            await Ejecutar(() => _perfiles.QuitarAsync(u.Id), "Foto de perfil quitada");
            return;
        }

        string? foto;
        try { foto = await FotoDePerfil.TomarValidadaAsync(); }
        catch (Exception ex) { await Dialogos.Aviso("No se pudo sacar la foto", TextosDeError.DeExcepcion(ex)); return; }
        if (foto is null) return;   // canceló

        await Ejecutar(() => _perfiles.GuardarAsync(u.Id, foto), "Foto de perfil actualizada");
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
