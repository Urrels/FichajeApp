namespace FichajeApp.Api.Infrastructure;

// Nombres y encabezados de las pestañas que usa la administración. Empleados y Fichajes ya existían;
// el resto se crea solo al iniciar la API (ver InicializadorPlanilla).
public static class Planilla
{
    public const string Empleados = "Empleados";
    public const string Fichajes = "Fichajes";

    public const string Roles = "Roles";
    public const string Permisos = "Permisos";
    public const string RolPermiso = "RolPermiso";
    public const string UsuarioRol = "UsuarioRol";
    public const string Bitacora = "Bitacora";
    public const string UsuarioHistorial = "UsuarioHistorial";
    public const string Parametros = "Parametros";
    public const string ValorHora = "ValorHora";
    public const string Feriados = "Feriados";
    public const string ExcedentesAprobados = "ExcedentesAprobados";
    public const string Liquidaciones = "Liquidaciones";
    public const string LiquidacionDias = "LiquidacionDias";

    public static readonly IReadOnlyDictionary<string, string[]> Encabezados = new Dictionary<string, string[]>
    {
        [Roles] = new[] { "Id", "Nombre", "PadreId", "Protegido" },
        [Permisos] = new[] { "Id", "Nombre" },
        [RolPermiso] = new[] { "RolId", "PermisoId" },
        [UsuarioRol] = new[] { "EmpleadoId", "RolId" },
        [Bitacora] = new[] { "FechaHora", "Usuario", "Accion" },
        [UsuarioHistorial] = new[] { "Id", "FechaHora", "EmpleadoId", "Tipo", "RealizadoPor", "Nombre", "Email", "Activo", "Bloqueado", "Intentos", "RolIds", "VersionOrigen" },
        [Parametros] = new[] { "Clave", "Valor", "Descripcion" },
        [ValorHora] = new[] { "EmpleadoId", "Valor", "VigenteDesde", "CargadoPor" },
        [Feriados] = new[] { "Fecha", "Descripcion" },
        [ExcedentesAprobados] = new[] { "Empleado", "Fecha", "AprobadoPor" },
        [Liquidaciones] = new[] { "Id", "Desde", "Hasta", "Estado", "CerradaPor", "FechaCierre", "PagadaPor", "FechaPago" },
        [LiquidacionDias] = new[]
        {
            "LiquidacionId", "EmpleadoId", "Empleado", "Fecha", "Ingreso", "Egreso", "MinTrabajados", "MinAsignados",
            "MinPagables", "MinNormales", "MinExtra", "MinFeriado", "MinExcedente", "ExcedenteAprobado", "Ausente",
            "ValorHora", "RecargoExtraPct", "RecargoFeriadoPct", "Importe", "Observacion"
        }
    };
}
