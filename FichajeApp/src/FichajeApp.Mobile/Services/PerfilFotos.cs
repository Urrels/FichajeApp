using System.Security.Cryptography;
using System.Text;

namespace FichajeApp.Mobile.Services;

// Fotos de perfil en el teléfono. El servidor dice quién tiene foto y de qué versión (una sola consulta); cada foto se baja
// una vez y queda guardada en el teléfono con su versión en el nombre: si alguien la cambia, la versión cambia y se baja de nuevo.
public class PerfilFotos
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);

    private readonly AdminApiService _api;
    private readonly IClienteService _clientes;
    private readonly SemaphoreSlim _candado = new(1, 1);

    private Dictionary<int, long> _versiones = new();
    private DateTime _cargadoEn = DateTime.MinValue;

    public PerfilFotos(AdminApiService api, IClienteService clientes)
    {
        _api = api;
        _clientes = clientes;
    }

    // Se avisa cuando cambió alguna foto, para que los avatares que están a la vista se refresquen
    public event Action? Cambio;

    // Cada servidor tiene su propia carpeta: los ids de un servidor no tienen nada que ver con los de otro
    private string Carpeta
    {
        get
        {
            var servidor = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_clientes.BaseUrl)))[..8];
            return Path.Combine(FileSystem.CacheDirectory, "perfiles", servidor);
        }
    }

    // Ruta de la foto en el teléfono, o null si la persona no tiene
    public async Task<string?> RutaAsync(int empleadoId)
    {
        if (empleadoId <= 0) return null;

        try
        {
            await AsegurarVersionesAsync();
            if (!_versiones.TryGetValue(empleadoId, out var version)) return null;

            var ruta = Path.Combine(Carpeta, $"{empleadoId}-{version}.jpg");
            if (File.Exists(ruta)) return ruta;

            if (!await _api.DescargarFotoDePerfil(empleadoId, ruta)) return null;
            BorrarVersionesViejas(empleadoId, ruta);
            return ruta;
        }
        catch { return null; }
    }

    // Una sola consulta a la vez: si veinte avatares piden a la vez, esperan la misma
    private async Task AsegurarVersionesAsync()
    {
        if (DateTime.UtcNow - _cargadoEn < Vigencia) return;

        await _candado.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _cargadoEn < Vigencia) return;

            var r = await _api.VersionesDePerfil();
            if (!r.Exito || r.Datos is null) return;   // sin conexión: se siguen usando las fotos que ya están en el teléfono

            _versiones = r.Datos.Where(p => int.TryParse(p.Key, out _)).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            _cargadoEn = DateTime.UtcNow;
        }
        finally { _candado.Release(); }
    }

    private void BorrarVersionesViejas(int empleadoId, string vigente)
    {
        try
        {
            foreach (var archivo in Directory.GetFiles(Carpeta, $"{empleadoId}-*.jpg").Where(a => a != vigente))
                File.Delete(archivo);
        }
        catch { /* es solo limpieza */ }
    }

    // empleadoId null = la foto propia
    public async Task<ResultadoConsulta<Respuesta>> GuardarAsync(int? empleadoId, string fotoBase64)
    {
        var r = await _api.GuardarFotoDePerfil(empleadoId, fotoBase64);
        if (r.Exito) Invalidar();
        return r;
    }

    public async Task<ResultadoConsulta<Respuesta>> QuitarAsync(int? empleadoId)
    {
        var r = await _api.QuitarFotoDePerfil(empleadoId);
        if (r.Exito) Invalidar();
        return r;
    }

    public void Invalidar()
    {
        _cargadoEn = DateTime.MinValue;
        Cambio?.Invoke();
    }
}
