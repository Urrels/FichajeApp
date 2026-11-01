using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

public record FeriadoExterno(DateTime Fecha, string Nombre, string Tipo);

// De dónde salen los feriados oficiales. Hoy ArgentinaDatos; detrás de una interfaz para cambiar de fuente o probar sin red.
public interface IProveedorFeriados
{
    Task<IReadOnlyList<FeriadoExterno>> ObtenerAsync(int anio, CancellationToken cancelar = default);
}

public class ArgentinaDatosProveedor : IProveedorFeriados
{
    private readonly HttpClient _http;

    public ArgentinaDatosProveedor(HttpClient http) => _http = http;

    // Todo puede venir null o con basura: lo que no se entiende se descarta en vez de romper toda la sincronización
    private class Item
    {
        [JsonPropertyName("fecha")] public string? Fecha { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("nombre")] public string? Nombre { get; set; }
    }

    public async Task<IReadOnlyList<FeriadoExterno>> ObtenerAsync(int anio, CancellationToken cancelar = default)
    {
        var items = await _http.GetFromJsonAsync<List<Item?>>($"v1/feriados/{anio}", cancelar) ?? new();
        var lista = new List<FeriadoExterno>();
        foreach (var i in items)
        {
            if (i?.Fecha is null || !DateTime.TryParseExact(i.Fecha, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fecha))
                continue;
            lista.Add(new FeriadoExterno(fecha, i.Nombre ?? string.Empty, i.Tipo ?? string.Empty));
        }
        return lista;
    }
}

public record FeriadoDto(string Fecha, string Nombre, string Tipo, string Origen, bool PagaRecargo);

// Feriados nacionales: se traen de la API a la pestaña Feriados (nunca se pisa lo cargado a mano) y se sirven a la app
// con cache de 24 h. La liquidación lee la pestaña, así que sigue funcionando si la API de feriados se cae.
public class FeriadosService
{
    private const int MaxDescripcion = 100;
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);

    private readonly ILiquidacionRepositorio _repositorio;
    private readonly IProveedorFeriados _proveedor;
    private readonly BitacoraService _bitacora;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _tiempo;
    private CancellationTokenSource _version = new();

    public FeriadosService(ILiquidacionRepositorio repositorio, IProveedorFeriados proveedor, BitacoraService bitacora,
        IMemoryCache cache, TimeProvider tiempo)
    {
        _repositorio = repositorio;
        _proveedor = proveedor;
        _bitacora = bitacora;
        _cache = cache;
        _tiempo = tiempo;
    }

    // Se llama cuando cambia un feriado, un parámetro o termina una sincronización: la próxima consulta lee de nuevo
    public void Invalidar() => Interlocked.Exchange(ref _version, new CancellationTokenSource()).Cancel();

    public async Task<List<FeriadoDto>> ObtenerAnioAsync(int anio)
    {
        if (anio < 2000 || anio > 2100) throw new InvalidOperationException("El año no es válido.");

        return (await _cache.GetOrCreateAsync($"feriados:{anio}", async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vigencia;
            entrada.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(_version.Token));

            var pagarPuentes = (await _repositorio.ObtenerParametrosAsync()).PagarPuentes;
            return (await _repositorio.ListarFeriadosAsync())
                .Where(f => !f.Excluido && f.Fecha.Year == anio)
                .OrderBy(f => f.Fecha)
                .Select(f => new FeriadoDto(f.Fecha.ToString("yyyy-MM-dd"), f.Descripcion, f.Tipo, f.Origen, f.PagaRecargo(pagarPuentes)))
                .ToList();
        }))!;
    }

    // Trae el año actual y el siguiente. Si falla el actual es un error (el siguiente suele no estar publicado hasta fin de año).
    public async Task<ResultadoSincronizacion> SincronizarAsync(string actor)
    {
        var hoy = _tiempo.GetLocalNow().DateTime;
        var oficiales = new List<Feriado>();

        foreach (var anio in new[] { hoy.Year, hoy.Year + 1 })
        {
            try
            {
                var externos = await _proveedor.ObtenerAsync(anio);
                // Se ignora lo que no es del año pedido: un dato mal ubicado no tiene que pisar otro año
                oficiales.AddRange(externos.Where(e => e.Fecha.Year == anio).Select(Convertir));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                if (anio == hoy.Year)
                    throw new InvalidOperationException("No pudimos consultar los feriados oficiales. Revisá la conexión a internet del servidor y probá de nuevo.");
            }
        }

        ResultadoSincronizacion r;
        try { r = await _repositorio.AplicarOficialesAsync(oficiales, hoy); }
        finally { Invalidar(); }   // aunque falle a mitad de camino, que no quede cache vieja

        if (r.Agregados + r.Actualizados + r.Retirados > 0)
            await _bitacora.RegistrarAsync(actor, $"FERIADOS_SINCRONIZADOS:+{r.Agregados} ~{r.Actualizados} -{r.Retirados}");
        return r;
    }

    private static Feriado Convertir(FeriadoExterno e) => new()
    {
        Fecha = e.Fecha.Date,
        Descripcion = LimpiarTexto(e.Nombre, MaxDescripcion, "Feriado"),
        Tipo = LimpiarTexto(e.Tipo, 30, string.Empty).ToLowerInvariant(),
        Origen = Feriado.OrigenApi
    };

    public static string LimpiarTexto(string? texto, int maximo, string porDefecto)
    {
        var limpio = new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (limpio.Length > maximo) limpio = limpio[..maximo];
        return limpio.Length == 0 ? porDefecto : limpio;
    }
}

// Una vez por día mantiene al día los feriados. Si falla (sin internet, planilla todavía sin crear) reintenta en 15 minutos
// en vez de esperar un día. Un error nunca tira la API.
public class FeriadosHostedService : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);   // deja que el inicializador cree las pestañas
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);
    private static readonly TimeSpan Reintento = TimeSpan.FromMinutes(15);

    private readonly FeriadosService _feriados;
    private readonly ILogger<FeriadosHostedService> _log;

    public FeriadosHostedService(FeriadosService feriados, ILogger<FeriadosHostedService> log)
    {
        _feriados = feriados;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        try
        {
            await Task.Delay(EsperaInicial, cancelar);
            while (true)
            {
                var espera = Intervalo;
                try
                {
                    var r = await _feriados.SincronizarAsync("sistema");
                    if (r.Agregados + r.Actualizados + r.Retirados > 0)
                        _log.LogInformation("Feriados sincronizados: {Agregados} nuevos, {Actualizados} actualizados, {Retirados} retirados.",
                            r.Agregados, r.Actualizados, r.Retirados);
                }
                catch (Exception ex) when (!cancelar.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "No se pudieron sincronizar los feriados; se reintenta en {Minutos} min", Reintento.TotalMinutes);
                    espera = Reintento;
                }
                await Task.Delay(espera, cancelar);
            }
        }
        catch (OperationCanceledException) { /* el servidor se está apagando */ }
    }
}
