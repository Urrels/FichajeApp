using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Infrastructure;

// "Migraciones" de la planilla: al iniciar la API crea las pestañas de administración que falten (con su
// encabezado) y siembra el catálogo de permisos, los roles iniciales y los parámetros de liquidación.
// Es idempotente: no toca nada de lo que ya existe. Se puede apagar con Planilla:AutoCrear = false.
public class InicializadorPlanilla : IHostedService
{
    private readonly GoogleSheetsClient _cliente;
    private readonly IRolesRepositorio _roles;
    private readonly IEmpleadosRepositorio _empleados;
    private readonly PermisosService _permisos;
    private readonly IConfiguration _config;
    private readonly ILogger<InicializadorPlanilla> _log;

    public InicializadorPlanilla(GoogleSheetsClient cliente, IRolesRepositorio roles, IEmpleadosRepositorio empleados,
        PermisosService permisos, IConfiguration config, ILogger<InicializadorPlanilla> log)
    {
        _cliente = cliente;
        _roles = roles;
        _empleados = empleados;
        _permisos = permisos;
        _config = config;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.GetValue("Planilla:AutoCrear", true)) return;

        try
        {
            await CrearPestanasAsync();
            await SembrarPermisosAsync();
            await SembrarRolesAsync();
            await SembrarParametrosAsync();
            await MigrarAsignacionesAsync();
            await GarantizarAdministradorAsync();
        }
        catch (Exception ex)
        {
            // La API sigue funcionando para fichar; la administración fallará hasta que se arregle la planilla
            _log.LogError(ex, "No se pudo inicializar la planilla de administración");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CrearPestanasAsync()
    {
        var planilla = await _cliente.Service.Spreadsheets.Get(_cliente.SpreadsheetId).ExecuteAsync();
        var existentes = planilla.Sheets.Select(s => s.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var faltantes = Planilla.Encabezados.Keys.Where(n => !existentes.Contains(n)).ToList();
        if (faltantes.Count == 0) return;

        await _cliente.Service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest
        {
            Requests = faltantes.Select(n => new Request
            {
                AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = n } }
            }).ToList()
        }, _cliente.SpreadsheetId).ExecuteAsync();

        foreach (var nombre in faltantes)
        {
            var pedido = _cliente.Service.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { Planilla.Encabezados[nombre].Cast<object>().ToList() } },
                _cliente.SpreadsheetId, $"{nombre}!A1");
            pedido.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await pedido.ExecuteAsync();
        }
        _log.LogInformation("Pestañas creadas en la planilla: {Pestanas}", string.Join(", ", faltantes));
    }

    private async Task SembrarPermisosAsync()
    {
        var existentes = (await _roles.ListarPermisosAsync()).Select(p => p.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var nombre in CatalogoPermisos.Todos.Where(n => !existentes.Contains(n)))
            await _roles.InsertarPermisoAsync(nombre);
    }

    private async Task SembrarRolesAsync()
    {
        if ((await _roles.ListarRolesAsync()).Count > 0) return;

        var permisos = (await _roles.ListarPermisosAsync()).ToDictionary(p => p.Nombre, p => p.Id);
        foreach (var (nombre, permisosDelRol) in CatalogoPermisos.RolesIniciales)
        {
            int id = await _roles.InsertarRolAsync(nombre, null, protegido: nombre == CatalogoPermisos.RolAdministrador);
            await _roles.GuardarPermisosDeRolAsync(id, permisosDelRol.Select(p => permisos[p]));
        }
    }

    private async Task SembrarParametrosAsync()
    {
        var hoja = new HojaSheets(_cliente, Planilla.Parametros);
        var existentes = (await hoja.LeerAsync("A:C")).Select(f => f.Texto(0)).ToHashSet();
        foreach (var (clave, valor, descripcion) in LiquidacionSheetsRepositorio.ParametrosIniciales.Where(p => !existentes.Contains(p.Clave)))
            await hoja.AgregarAsync(new List<object> { clave, (double)valor, descripcion });
    }

    // Primera vez: cada empleado queda con el rol que indicaba la columna F (Administrador / Supervisor / Empleado)
    private async Task MigrarAsignacionesAsync()
    {
        if ((await _roles.ListarAsignacionesAsync()).Count > 0) return;

        var roles = await _roles.ListarRolesAsync();
        foreach (var e in await _empleados.ListarAsync())
        {
            var ids = PermisosService.RolesEfectivos(e, new Dictionary<int, List<int>>(), roles);
            if (ids.Count > 0) await _roles.ReemplazarAsignacionesAsync(e.Id, ids);
        }
    }

    // Si nadie puede administrar, se promueve al usuario indicado en Planilla:AdministradorInicial
    private async Task GarantizarAdministradorAsync()
    {
        var usuarios = await _permisos.RolesDeUsuariosActivosAsync();
        var permisosPorRol = await _permisos.PermisosPorRolAsync();

        bool hayAdministrador = usuarios.Values.Any(roles => roles.Any(r =>
            permisosPorRol.TryGetValue(r, out var p) && CatalogoPermisos.DeAdministracion.All(p.Contains)));
        if (hayAdministrador) return;

        var email = _config["Planilla:AdministradorInicial"];
        if (string.IsNullOrWhiteSpace(email))
        {
            _log.LogWarning("Ningún usuario activo puede administrar el sistema. Poné \"Administrador\" en la columna F de " +
                            "su fila en la hoja Empleados, o configurá Planilla:AdministradorInicial con su email.");
            return;
        }

        var empleado = await _empleados.BuscarPorEmailAsync(email);
        var admin = (await _roles.ListarRolesAsync()).FirstOrDefault(r => r.Nombre == CatalogoPermisos.RolAdministrador);
        if (empleado is null || admin is null)
        {
            _log.LogWarning("Planilla:AdministradorInicial ({Email}) no corresponde a un empleado activo.", email);
            return;
        }

        var actuales = (await _roles.ListarAsignacionesAsync()).GetValueOrDefault(empleado.Id) ?? new List<int>();
        await _roles.ReemplazarAsignacionesAsync(empleado.Id, actuales.Append(admin.Id));
        _log.LogWarning("{Email} quedó como Administrador porque no había ningún administrador activo.", email);
    }
}
