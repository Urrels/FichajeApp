namespace FichajeApp.Mobile.Services;

// Nombres de los permisos del catálogo del servidor (deben coincidir con CatalogoPermisos de la API)
public static class NombresPermisos
{
    public const string AdministrarUsuarios = "Administrar usuarios";
    public const string GestionRoles = "Gestión de roles";
    public const string VerBitacora = "Ver bitácora";
    public const string CambiarPin = "Cambiar PIN";
    public const string FicharAsistencia = "Fichar asistencia";
    public const string VerPanelSupervisor = "Ver panel de supervisor";
    public const string CorregirFichajes = "Corregir fichajes";
    public const string LiquidarHoras = "Liquidar horas";
    public const string GestionarApariencia = "Gestionar apariencia";
    public const string ConfigurarFichaje = "Configurar fichaje";
    public const string FicharEnKiosco = "Fichar en kiosco";
    public const string CambiarConexion = "Cambiar conexión de Google";

    // Los que dan acceso al área de administración
    public static readonly string[] DeAdministracion =
        { AdministrarUsuarios, GestionRoles, VerBitacora, CorregirFichajes, LiquidarHoras, GestionarApariencia, ConfigurarFichaje };
}

// A qué pantalla va cada usuario al entrar
public static class Navegacion
{
    public static string RutaInicial()
    {
        var sesion = SessionManager.Instance;
        if (sesion.EsKiosco) return "//KioscoPage";
        if (sesion.EsSupervisor) return "//SupervisorPage";
        if (sesion.EsAdministrador) return "//AdminPage";
        return "//FichajePage";
    }
}
