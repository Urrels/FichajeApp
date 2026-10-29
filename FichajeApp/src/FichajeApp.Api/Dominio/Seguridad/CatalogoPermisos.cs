namespace FichajeApp.Api.Dominio.Seguridad;

// Catálogo de permisos del sistema. Se siembra en la hoja "Permisos" y nunca se crea ni se
// elimina desde la aplicación (igual que en ingenieria_software). Los nombres son la clave
// que viaja en el token y con la que se protegen los endpoints.
public static class CatalogoPermisos
{
    // Seguridad
    public const string AdministrarUsuarios = "Administrar usuarios";
    public const string GestionRoles = "Gestión de roles";
    public const string VerBitacora = "Ver bitácora";
    public const string CambiarPin = "Cambiar PIN";

    // Negocio
    public const string FicharAsistencia = "Fichar asistencia";
    public const string VerPanelSupervisor = "Ver panel de supervisor";
    public const string CorregirFichajes = "Corregir fichajes";
    public const string LiquidarHoras = "Liquidar horas";
    public const string GestionarApariencia = "Gestionar apariencia";
    public const string ConfigurarFichaje = "Configurar fichaje";

    // Teléfono compartido de una sucursal: cada empleado ficha eligiendo su nombre y poniendo su PIN
    public const string FicharEnKiosco = "Fichar en kiosco";

    public static readonly string[] Todos =
    {
        AdministrarUsuarios, GestionRoles, VerBitacora, CambiarPin,
        FicharAsistencia, VerPanelSupervisor, CorregirFichajes, LiquidarHoras, GestionarApariencia, ConfigurarFichaje,
        FicharEnKiosco
    };

    // Permisos sin los cuales el sistema quedaría sin administración (guarda anti-lockout)
    public static readonly string[] DeAdministracion = { AdministrarUsuarios, GestionRoles };

    public const string RolAdministrador = "Administrador";
    public const string RolSupervisor = "Supervisor";
    public const string RolEmpleado = "Empleado";
    public const string RolKiosco = "Kiosco";

    // Roles iniciales y sus permisos. Administrador es protegido y lo tiene todo.
    public static readonly IReadOnlyDictionary<string, string[]> RolesIniciales = new Dictionary<string, string[]>
    {
        [RolAdministrador] = Todos,
        [RolSupervisor] = new[] { FicharAsistencia, VerPanelSupervisor, CorregirFichajes, CambiarPin },
        [RolEmpleado] = new[] { FicharAsistencia, CambiarPin },
        [RolKiosco] = new[] { FicharEnKiosco }
    };
}
