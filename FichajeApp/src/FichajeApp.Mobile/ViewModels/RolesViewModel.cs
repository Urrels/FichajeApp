using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Una fila del árbol de roles, aplanada con su nivel de sangría
public class FilaRol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? PadreId { get; set; }
    public int Nivel { get; set; }
    public bool Protegido { get; set; }
    public int CantidadPermisos { get; set; }
    public List<string> Permisos { get; set; } = new();

    public string Texto => new string(' ', Nivel * 4) + (Nivel > 0 ? "↳ " : "") + Nombre + (Protegido ? " 🔒" : "");
    public string Detalle => new string(' ', Nivel * 4) + (CantidadPermisos == 0 ? "Sin permisos" : string.Join(", ", Permisos));
}

public class RolesViewModel : VmBase
{
    private readonly AdminApiService _api;

    public RolesViewModel(AdminApiService api) => _api = api;

    public ObservableCollection<FilaRol> Roles { get; } = new();

    public Task CargarAsync() => ConEspera(async () =>
    {
        Estado = "Cargando...";
        var r = await _api.ArbolDeRoles();

        Roles.Clear();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        foreach (var raiz in r.Datos.Where(n => n.EsRol)) Aplanar(raiz, 0);
        Estado = Roles.Count == 0 ? "No hay roles" : $"{Roles.Count} rol(es)";
    });

    private void Aplanar(NodoRol rol, int nivel)
    {
        var permisos = rol.Hijos.Where(h => !h.EsRol).Select(h => h.Nombre).OrderBy(n => n).ToList();
        Roles.Add(new FilaRol
        {
            Id = rol.Id, Nombre = rol.Nombre, PadreId = rol.PadreId, Nivel = nivel, Protegido = rol.Protegido,
            CantidadPermisos = permisos.Count, Permisos = permisos
        });
        foreach (var hijo in rol.Hijos.Where(h => h.EsRol).OrderBy(h => h.Nombre))
            Aplanar(hijo, nivel + 1);
    }

    public async Task NuevoAsync()
    {
        var nombre = await Dialogos.Texto("Nuevo rol", "Nombre del rol", maximo: 50);
        if (string.IsNullOrEmpty(nombre)) return;
        await Ejecutar(() => _api.CrearRol(nombre, null), "Rol creado");
    }

    public async Task AccionesAsync(FilaRol rol)
    {
        var opciones = new List<string> { "Permisos", "Agregar subrol", "Duplicar", "Cambiar padre" };
        var elegida = await Dialogos.Elegir(rol.Nombre, opciones, rol.Protegido ? null : "Eliminar");

        switch (elegida)
        {
            case "Permisos": await PermisosAsync(rol); break;
            case "Agregar subrol":
                var sub = await Dialogos.Texto("Nuevo subrol", $"Nombre del subrol de {rol.Nombre}", maximo: 50);
                if (!string.IsNullOrEmpty(sub)) await Ejecutar(() => _api.CrearRol(sub, rol.Id), "Subrol creado");
                break;
            case "Duplicar":
                var copia = await Dialogos.Texto("Duplicar rol", "Nombre de la copia (lleva los mismos permisos)", $"{rol.Nombre} (copia)", 50);
                if (!string.IsNullOrEmpty(copia)) await Ejecutar(() => _api.DuplicarRol(rol.Id, copia), "Rol duplicado");
                break;
            case "Cambiar padre": await CambiarPadreAsync(rol); break;
            case "Eliminar":
                if (await Dialogos.Confirmar("Eliminar rol", $"Se elimina «{rol.Nombre}» con sus subroles y permisos. Solo se puede si nadie lo tiene asignado.", "Eliminar"))
                    await Ejecutar(() => _api.EliminarRol(rol.Id), "Rol eliminado");
                break;
        }
    }

    private async Task PermisosAsync(FilaRol rol)
    {
        var catalogo = await _api.CatalogoDePermisos();
        var actuales = await _api.PermisosDeRol(rol.Id);
        if (!catalogo.Exito || catalogo.Datos is null) { await Dialogos.Aviso("No se pudo", catalogo.Mensaje); return; }
        if (!actuales.Exito || actuales.Datos is null) { await Dialogos.Aviso("No se pudo", actuales.Mensaje); return; }

        // Los permisos son propios de cada rol: el padre no se los transmite al hijo
        var elegidos = await Dialogos.ElegirVarios($"Permisos de {rol.Nombre}",
            catalogo.Datos.OrderBy(p => p.Nombre).Select(p => (p.Id, p.Nombre)).ToList(), actuales.Datos);
        if (elegidos is null) return;

        await Ejecutar(() => _api.GuardarPermisos(rol.Id, elegidos), "Permisos actualizados");
    }

    private async Task CambiarPadreAsync(FilaRol rol)
    {
        const string SinPadre = "(Sin padre)";
        var candidatos = Roles.Where(r => r.Id != rol.Id).ToDictionary(r => r.Nombre, r => (int?)r.Id);
        var opciones = new List<string> { SinPadre };
        opciones.AddRange(candidatos.Keys);

        var elegida = await Dialogos.Elegir($"Nuevo padre de {rol.Nombre}", opciones);
        if (elegida is null) return;

        int? padre = elegida == SinPadre ? null : candidatos[elegida];
        await Ejecutar(() => _api.CambiarPadre(rol.Id, padre), "Rol movido");
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
