using System.Text.Json.Serialization;

namespace FichajeApp.Api.Models;

public class EmpleadoDto
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PinHash { get; set; } = default!;
    public bool Activo { get; set; }

    // Texto de la columna F de la hoja Empleados. Solo se usa como rol inicial de quien todavía no
    // tiene roles asignados ("Administrador", "Supervisor" o vacío = Empleado); los roles reales
    // se gestionan desde la hoja UsuarioRol.
    public string Rol { get; set; } = "Empleado";

    // Bloqueo por intentos fallidos de PIN (columnas G y H)
    public int IntentosFallidos { get; set; }
    public bool Bloqueado { get; set; }

    // Jerarquía: quién tiene a cargo a esta persona. null = nadie en particular (queda a cargo del administrador).
    // Solo se muestra al supervisor cuya persona le fue asignada; el administrador ve a todos.
    public int? SupervisorId { get; set; }

    // Número de fila en la hoja, para poder actualizarla
    [JsonIgnore]
    public int Fila { get; set; }

    // Copia completa. Un solo lugar: antes cada caché y cada prueba tenía su propia copia campo por campo y agregar un
    // campo nuevo (como SupervisorId) se perdía en silencio en la que se olvidaban.
    public EmpleadoDto Clonar() => (EmpleadoDto)MemberwiseClone();
}
