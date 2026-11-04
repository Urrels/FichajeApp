using System.Text.RegularExpressions;
using FichajeApp.Api.Dominio.Apariencia;

namespace FichajeApp.Api.Storage;

// Fotos de perfil: una por persona, en datos/perfiles/<id>.jpg|png. A diferencia de las fotos de los fichajes, estas NO se borran
// solas con el tiempo: duran hasta que alguien las cambie o las quite.
public interface IAlmacenPerfiles
{
    Task GuardarAsync(int empleadoId, byte[] bytes);

    // null si esa persona no tiene foto. Version cambia cada vez que se guarda una nueva (sirve para refrescar la copia del teléfono).
    Task<(byte[] Bytes, string Mime, long Version)?> LeerAsync(int empleadoId);

    Task<bool> EliminarAsync(int empleadoId);

    // id de cada persona que tiene foto -> versión de esa foto
    Task<IReadOnlyDictionary<int, long>> VersionesAsync();
}

public partial class AlmacenPerfilesEnDisco : IAlmacenPerfiles
{
    [GeneratedRegex(@"^(\d{1,9})\.(jpg|png)$")]
    private static partial Regex NombreValido();

    private readonly string _carpeta;

    public AlmacenPerfilesEnDisco(IConfiguration config, IHostEnvironment entorno)
        : this(Path.Combine(config["Setup:Carpeta"] ?? Path.Combine(entorno.ContentRootPath, "datos"), "perfiles")) { }

    public AlmacenPerfilesEnDisco(string carpeta) => _carpeta = Path.GetFullPath(carpeta);

    public async Task GuardarAsync(int empleadoId, byte[] bytes)
    {
        var extension = ReglasApariencia.DetectarFormato(bytes) == ReglasApariencia.FormatoLogo.Png ? "png" : "jpg";
        Directory.CreateDirectory(_carpeta);

        // Se escribe a un archivo temporal y se renombra: quien la está leyendo nunca ve una foto a medio escribir
        var destino = Ruta(empleadoId, extension);
        var temporal = destino + ".tmp";
        await File.WriteAllBytesAsync(temporal, bytes);
        File.Move(temporal, destino, overwrite: true);

        // Si antes tenía una de otro formato, esa sobra
        var otra = Ruta(empleadoId, extension == "png" ? "jpg" : "png");
        if (File.Exists(otra)) File.Delete(otra);
    }

    public async Task<(byte[] Bytes, string Mime, long Version)?> LeerAsync(int empleadoId)
    {
        foreach (var extension in new[] { "jpg", "png" })
        {
            var ruta = Ruta(empleadoId, extension);
            if (!File.Exists(ruta)) continue;
            return (await File.ReadAllBytesAsync(ruta), extension == "png" ? "image/png" : "image/jpeg", VersionDe(ruta));
        }
        return null;
    }

    public Task<bool> EliminarAsync(int empleadoId)
    {
        var borro = false;
        foreach (var extension in new[] { "jpg", "png" })
        {
            var ruta = Ruta(empleadoId, extension);
            if (!File.Exists(ruta)) continue;
            File.Delete(ruta);
            borro = true;
        }
        return Task.FromResult(borro);
    }

    public Task<IReadOnlyDictionary<int, long>> VersionesAsync()
    {
        var resultado = new Dictionary<int, long>();
        if (Directory.Exists(_carpeta))
            foreach (var archivo in Directory.GetFiles(_carpeta))
            {
                // Solo los archivos que creó este almacén: <número>.jpg|png
                var m = NombreValido().Match(Path.GetFileName(archivo));
                if (m.Success) resultado[int.Parse(m.Groups[1].Value)] = VersionDe(archivo);
            }
        return Task.FromResult<IReadOnlyDictionary<int, long>>(resultado);
    }

    private string Ruta(int empleadoId, string extension) => Path.Combine(_carpeta, $"{empleadoId}.{extension}");

    // Milisegundos de la última escritura: alcanza para distinguir una foto de la que la reemplazó
    private static long VersionDe(string ruta) => new DateTimeOffset(File.GetLastWriteTimeUtc(ruta)).ToUnixTimeMilliseconds();
}
