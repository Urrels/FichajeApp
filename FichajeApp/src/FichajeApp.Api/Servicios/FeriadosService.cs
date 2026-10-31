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

    private class Item
    {
        [JsonPropertyName("fecha")] public string Fecha { get; set; } = string.Empty;
        [JsonPropertyName("tipo")] public string Tipo { get; set; } = string.Empty;
        [JsonPropertyName("nombre")] public string Nombre { get; set; } = string.Empty;
    }

    public async Task<IReadOnlyList<FeriadoExterno>> ObtenerAsync(int anio, CancellationToken cancelar = default)
    {
        var items = await _http.GetFromJsonAsync<List<Item>>($"v1/feriados/{anio}", cancelar) ?? new();
        return items
            .Where(i => DateTime.TryParseExact(i.Fecha, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
            .Select(i => new FeriadoExterno(
                DateTime.ParseExact(i.Fecha, "yyyy-MM-dd", null), i.Nombre.Trim(), i.Tipo.Trim().ToLowerInvariant()))
            .ToList();
    }
}

public record FeriadoDto(string Fecha, string Nombre, string Tipo, string Origen, bool PagaRecargo);
public record ResultadoSincronizacion(int Agregados, int Actualizados, int Omitidos);

// Feriados nacionales: se traen de la API a la pestaña Feriados (nunca se pisa lo cargado a mano) y se sirven a la app
// con cache de 24 h. La liquidación lee la pestaña, así que sigue funcionando si la API de feriados se cae.
public class FeriadosService
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim _sincronizacion = new(1, 1);

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
                .Where(f => f.Fecha.Year == anio)
                .OrderBy(f => f.Fecha)
                .Select(f => new FeriadoDto(f.Fecha.ToString("yyyy-MM-dd"), f.Descripcion, f.Tipo, f.Origen, f.PagaRecargo(pagarPuentes)))
                .ToList();
        }))!;
    }

    // Trae el año actual y el siguiente. Agrega lo que falta y actualiza lo que vino antes de la API; lo manual se respeta.
    public async Task<ResultadoSincronizacion> SincronizarAsync(string actor)
    {
        int anio = _tiempo.GetLocalNow().Year;
        int agregados = 0, actualizados = 0, omitidos = 0, consultados = 0;

        await _sincronizacion.WaitAsync();
        try
        {
            var actuales = (await _repositorio.ListarFeriadosAsync()).ToDictionary(f => f.Fecha.Date);

            foreach (var a in new[] { anio, anio + 1 })
            {
                IReadOnlyList<FeriadoExterno> externos;
                try { externos = await _proveedor.ObtenerAsync(a); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    continue; // el año siguiente suele no estar publicado hasta fin de año: no es un error
                }
                consultados++;

                foreach (var e in externos)
                {
                    var nuevo = new Feriado { Fecha = e.Fecha.Date, Descripcion = e.Nombre, Tipo = e.Tipo, Origen = Feriado.OrigenApi };
                    if (!actuales.TryGetValue(nuevo.Fecha, out var existente))
                    {
                        await _repositorio.GuardarFeriadoAsync(nuevo);
                        actuales[nuevo.Fecha] = nuevo;
                        agregados++;
                    }
                    else if (existente.Origen == Feriado.OrigenManual)
                        omitidos++;
                    else if (existente.Descripcion != nuevo.Descripcion || existente.Tipo != nuevo.Tipo)
                    {
                        await _repositorio.GuardarFeriadoAsync(nuevo);
                        actuales[nuevo.Fecha] = nuevo;
                        actualizados++;
                    }
                }
            }
        }
        finally { _sincronizacion.Release(); }

        if (consultados == 0)
            throw new InvalidOperationException("No pudimos consultar los feriados oficiales. Revisá la conexión a internet del servidor y probá de nuevo.");

        Invalidar();
        if (agregados + actualizados > 0)
            await _bitacora.RegistrarAsync(actor, $"FERIADOS_SINCRONIZADOS:+{agregados} ~{actualizados}");
        return new ResultadoSincronizacion(agregados, actualizados, omitidos);
    }
}

// Una vez por día (y al arrancar) mantiene al día los feriados. Un error de red o de Google no tira la API.
public class FeriadosHostedService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    private readonly FeriadosService _feriados;
    private readonly ILogger<FeriadosHostedService> _log;

    public FeriadosHostedService(FeriadosService feriados, ILogger<FeriadosHostedService> log)
    {
        _feriados = feriados;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken cancelar)
    {
        using var reloj = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                var r = await _feriados.SincronizarAsync("sistema");
                if (r.Agregados + r.Actualizados > 0)
                    _log.LogInformation("Feriados sincronizados: {Agregados} nuevos, {Actualizados} actualizados.", r.Agregados, r.Actualizados);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudieron sincronizar los feriados");
            }
        }
        while (await reloj.WaitForNextTickAsync(cancelar));
    }
}
