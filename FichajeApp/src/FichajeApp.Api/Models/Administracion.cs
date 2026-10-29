namespace FichajeApp.Api.Models;

public enum LoginResultado { Exito, CredencialesInvalidas, UsuarioBloqueado }

public class ResultadoLogin
{
    public LoginResultado Resultado { get; set; }
    public EmpleadoDto? Empleado { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permisos { get; set; } = new();
}

public class PaginaResultado<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int Tamanio { get; set; }
}

public class EntradaBitacora
{
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
}

// Foto del estado de un usuario en un momento (historial de solo inserción). El PIN nunca se guarda.
public class HistorialUsuario
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }
    public int EmpleadoId { get; set; }
    public string Tipo { get; set; } = string.Empty; // ALTA, BAJA, BLOQUEO, DESBLOQUEO, CAMBIO_PIN, ASIGNACION_ROL, EDICION_DATOS, ROLLBACK
    public string RealizadoPor { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool Bloqueado { get; set; }
    public int IntentosFallidos { get; set; }
    public List<int> RolIds { get; set; } = new();
    public int? VersionOrigen { get; set; }
}

public class UsuarioAdminDto
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool Bloqueado { get; set; }
    public int IntentosFallidos { get; set; }
    public List<int> RolIds { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}

// Un fichaje de la hoja con su identificador (número de fila) y datos de corrección
public class FichajeRegistrado
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Lugar { get; set; } = string.Empty;
    public bool Corregido { get; set; }
    public string? MotivoCorreccion { get; set; }
}
