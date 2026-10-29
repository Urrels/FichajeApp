using System.Text.RegularExpressions;
using FichajeApp.Api.Dominio.Apariencia;

namespace FichajeApp.Api.Storage;

// Dónde se guardan las fotos de los fichajes. Hoy el disco del servidor; mañana otra implementación
// (por ejemplo Amazon S3) sin tocar el resto.
public interface IAlmacenFotos
{
    // Guarda la foto y devuelve su nombre relativo (es lo que queda anotado en la planilla)
    Task<string> GuardarAsync(byte[] bytes, DateTime fecha);

    // null si no existe o el nombre no es válido
    Task<(byte[] Bytes, string Mime)?> LeerAsync(string nombre);

    // Borra las fotos de los días anteriores a la fecha dada; devuelve cuántas borró
    Task<int> EliminarAnterioresAsync(DateTime fecha);
}

// Reglas de las fotos: mismas imágenes que el logo (PNG o JPG), con un tope de tamaño
public static class ReglasFotos
{
    public const int TamanioMaximoBytes = 400 * 1024;

    // Acepta "data:image/jpeg;base64,..." o base64 pelado
    public static byte[] Decodificar(string base64)
    {
        var coma = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma >= 0) base64 = base64[(coma + 1)..];

        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new FormatException("La foto no es válida."); }

        if (bytes.Length > TamanioMaximoBytes)
            throw new FormatException($"La foto es demasiado pesada (máximo {TamanioMaximoBytes / 1024} KB).");
        if (ReglasApariencia.DetectarFormato(bytes) == ReglasApariencia.FormatoLogo.Desconocido)
            throw new FormatException("La foto tiene que ser una imagen PNG o JPG.");
        return bytes;
    }
}

public partial class AlmacenFotosEnDisco : IAlmacenFotos
{
    // aaaa/mm/dd/<id>.jpg|png : solo nombres que generó este almacén (nada de rutas armadas por el cliente)
    [GeneratedRegex(@"^\d{4}/\d{2}/\d{2}/[0-9a-f]{32}\.(jpg|png)$")]
    private static partial Regex NombreValido();

    private readonly string _carpeta;

    public AlmacenFotosEnDisco(IConfiguration config, IHostEnvironment entorno)
        : this(Path.Combine(config["Setup:Carpeta"] ?? Path.Combine(entorno.ContentRootPath, "datos"), "fotos")) { }

    public AlmacenFotosEnDisco(string carpeta) => _carpeta = Path.GetFullPath(carpeta);

    public async Task<string> GuardarAsync(byte[] bytes, DateTime fecha)
    {
        var extension = ReglasApariencia.DetectarFormato(bytes) == ReglasApariencia.FormatoLogo.Png ? "png" : "jpg";
        var nombre = $"{fecha:yyyy/MM/dd}/{Guid.NewGuid():N}.{extension}";

        var ruta = RutaDe(nombre)!;
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllBytesAsync(ruta, bytes);
        return nombre;
    }

    public async Task<(byte[] Bytes, string Mime)?> LeerAsync(string nombre)
    {
        var ruta = RutaDe(nombre);
        if (ruta is null || !File.Exists(ruta)) return null;

        var bytes = await File.ReadAllBytesAsync(ruta);
        return (bytes, nombre.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
    }

    public Task<int> EliminarAnterioresAsync(DateTime fecha)
    {
        int borradas = 0;
        if (!Directory.Exists(_carpeta)) return Task.FromResult(0);

        // Solo se tocan las carpetas aaaa/mm/dd que creó este almacén
        foreach (var anio in Directory.GetDirectories(_carpeta).Where(d => EsNumero(d, 4)))
        {
            foreach (var mes in Directory.GetDirectories(anio).Where(d => EsNumero(d, 2)))
            {
                foreach (var dia in Directory.GetDirectories(mes).Where(d => EsNumero(d, 2)))
                {
                    if (!DateTime.TryParse($"{Path.GetFileName(anio)}-{Path.GetFileName(mes)}-{Path.GetFileName(dia)}",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dia_)
                        || dia_.Date >= fecha.Date) continue;

                    borradas += Directory.GetFiles(dia).Length;
                    Directory.Delete(dia, recursive: true);
                }
                if (!Directory.EnumerateFileSystemEntries(mes).Any()) Directory.Delete(mes);
            }
            if (!Directory.EnumerateFileSystemEntries(anio).Any()) Directory.Delete(anio);
        }
        return Task.FromResult(borradas);
    }

    private static bool EsNumero(string carpeta, int largo)
    {
        var nombre = Path.GetFileName(carpeta);
        return nombre.Length == largo && nombre.All(char.IsDigit);
    }

    private string? RutaDe(string nombre)
    {
        if (!NombreValido().IsMatch(nombre)) return null;

        var ruta = Path.GetFullPath(Path.Combine(_carpeta, nombre));
        return ruta.StartsWith(_carpeta + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? ruta : null;
    }
}
