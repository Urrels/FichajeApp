using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Models;
using FichajeApp.Api.Services;

namespace FichajeApp.Api.Servicios;

// Un lugar conocido tal como lo ve la administración (con su identificador)
public record SitioAdmin(int Id, string Nombre, double Latitud, double Longitud, double RadioMetros);

public record GuardarSitioRequest(string? Nombre, double Latitud, double Longitud, double RadioMetros);

// Alta, cambio y baja de los lugares conocidos (donde se puede fichar con "Ubicación libre").
// Se guardan en la base SQL.
public interface ISitiosAdminRepositorio
{
    Task<List<SitioAdmin>> ListarAsync();
    Task<int> CrearAsync(Sitio sitio);
    Task ActualizarAsync(int id, Sitio sitio);   // InvalidOperationException si no existe
    Task EliminarAsync(int id);                  // InvalidOperationException si no existe
}

public class SitiosAdminService
{
    public const int LargoMaximoNombre = 100;
    public const double RadioMinimo = 10, RadioMaximo = 5000;

    private readonly ISitiosAdminRepositorio _repositorio;
    private readonly BitacoraService _bitacora;
    private readonly IMemoryCache _cache;

    public SitiosAdminService(ISitiosAdminRepositorio repositorio, BitacoraService bitacora, IMemoryCache cache)
    {
        _repositorio = repositorio;
        _bitacora = bitacora;
        _cache = cache;
    }

    public Task<List<SitioAdmin>> ListarAsync() => _repositorio.ListarAsync();

    public async Task<int> CrearAsync(string actor, GuardarSitioRequest request)
    {
        var sitio = await ValidarAsync(request, ignorarId: null);
        var id = await _repositorio.CrearAsync(sitio);
        await Registrar(actor, "SITIO_CREADO", sitio.Nombre);
        return id;
    }

    public async Task ActualizarAsync(string actor, int id, GuardarSitioRequest request)
    {
        var sitio = await ValidarAsync(request, ignorarId: id);
        await _repositorio.ActualizarAsync(id, sitio);
        await Registrar(actor, "SITIO_EDITADO", sitio.Nombre);
    }

    public async Task EliminarAsync(string actor, int id)
    {
        var nombre = (await _repositorio.ListarAsync()).FirstOrDefault(s => s.Id == id)?.Nombre
            ?? throw new InvalidOperationException("El lugar no existe.");
        await _repositorio.EliminarAsync(id);
        await Registrar(actor, "SITIO_ELIMINADO", nombre);
    }

    private async Task<Sitio> ValidarAsync(GuardarSitioRequest r, int? ignorarId)
    {
        var nombre = (r.Nombre ?? string.Empty).Trim();
        if (nombre.Length == 0) throw new InvalidOperationException("Indicá el nombre del lugar.");
        if (nombre.Length > LargoMaximoNombre)
            throw new InvalidOperationException($"El nombre no puede superar los {LargoMaximoNombre} caracteres.");

        if (double.IsNaN(r.Latitud) || double.IsNaN(r.Longitud) || r.Latitud is < -90 or > 90 || r.Longitud is < -180 or > 180)
            throw new InvalidOperationException("La ubicación no es válida: la latitud va de -90 a 90 y la longitud de -180 a 180.");
        // (0, 0) es lo que llega cuando no se pudo obtener la ubicación: no es un lugar de trabajo
        if (r.Latitud == 0 && r.Longitud == 0)
            throw new InvalidOperationException("La ubicación no es válida: no se pudo obtener la posición del lugar.");

        if (double.IsNaN(r.RadioMetros) || r.RadioMetros < RadioMinimo || r.RadioMetros > RadioMaximo)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"El radio tiene que estar entre {RadioMinimo:0} y {RadioMaximo:0} metros."));

        var repetido = (await _repositorio.ListarAsync())
            .Any(s => s.Id != ignorarId && s.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        if (repetido) throw new InvalidOperationException("Ya hay un lugar con ese nombre.");

        return new Sitio(nombre, r.Latitud, r.Longitud, r.RadioMetros);
    }

    // El fichaje consulta la lista cacheada: sin esto un lugar nuevo tardaría minutos en reconocerse
    private async Task Registrar(string actor, string accion, string nombre)
    {
        CachedSitiosRepository.Invalidar(_cache);
        await _bitacora.RegistrarAsync(actor, $"{accion}:{nombre}");
    }
}
