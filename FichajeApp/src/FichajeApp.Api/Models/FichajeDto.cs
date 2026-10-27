namespace FichajeApp.Api.Models;

// Representa un fichaje completo, ya resuelto (se usa para persistir y para notificar observers)
public class FichajeDto
{
    public string NombreCompleto { get; set; } = default!;
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public DateTime FechaHora { get; set; } = DateTime.Now;
    public string TipoFichaje { get; set; } = "Entrada";
    public string Lugar { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public double? Precision { get; set; }
    public string? Direccion { get; set; }
}

// Lo único que manda el cliente: el nombre y el tipo (Entrada/Salida) los decide el backend
public class FichajeRequestDto
{
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
    public bool EsUbicacionFalsa { get; set; }
    public string? Direccion { get; set; }
    public string? Motivo { get; set; }
}

// Contexto que viaja por la cadena de validaciones (patrón Composite)
public class FichajeContext
{
    public EmpleadoDto? Empleado { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
    public bool EsUbicacionFalsa { get; set; }
    public string? Motivo { get; set; }
    public string? Direccion { get; set; }
    public string? Sitio { get; set; }
}

public class ResultadoValidacion
{
    public bool EsValido { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string? Codigo { get; set; }

    public static ResultadoValidacion Ok() => new() { EsValido = true };
    public static ResultadoValidacion Error(string mensaje, string? codigo = null) => new() { EsValido = false, Mensaje = mensaje, Codigo = codigo };
}
