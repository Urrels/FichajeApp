using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Concurrency;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Tests;

public class CachedEmpleadosRepositorioTests
{
    private class ContadorDeLecturas : IEmpleadosRepositorio
    {
        public EmpleadosFalso Real { get; } = new();
        public int Lecturas { get; private set; }

        public Task<List<EmpleadoDto>> ListarAsync() { Lecturas++; return Real.ListarAsync(); }
        public Task<EmpleadoDto?> ObtenerPorIdAsync(int id) => Real.ObtenerPorIdAsync(id);
        public Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false) => Real.BuscarPorEmailAsync(email, incluirInactivos);
        public Task<EmpleadoDto> CrearAsync(EmpleadoDto e) => Real.CrearAsync(e);
        public Task ActualizarAsync(EmpleadoDto e) => Real.ActualizarAsync(e);
    }

    private static (CachedEmpleadosRepositorio Cache, ContadorDeLecturas Origen) Crear()
    {
        var origen = new ContadorDeLecturas();
        origen.Real.Datos.Add(new EmpleadoDto { Id = 1, NombreCompleto = "Ana", Email = "ana@test.com", PinHash = "x", Activo = true });
        return (new CachedEmpleadosRepositorio(origen, new MemoryCache(new MemoryCacheOptions())), origen);
    }

    [Fact]
    public async Task VariasLecturas_LeenLaHojaUnaSolaVez()
    {
        var (cache, origen) = Crear();

        await cache.ListarAsync();
        await cache.BuscarPorEmailAsync("ana@test.com");
        await cache.ObtenerPorIdAsync(1);

        Assert.Equal(1, origen.Lecturas);
    }

    [Fact]
    public async Task ModificarLoQueSeLeyo_NoCorrompeLaCache()
    {
        var (cache, _) = Crear();

        var leido = (await cache.BuscarPorEmailAsync("ana@test.com"))!;
        leido.IntentosFallidos = 99; // por ejemplo, el login suma un intento y la escritura después falla

        Assert.Equal(0, (await cache.BuscarPorEmailAsync("ana@test.com"))!.IntentosFallidos);
    }

    [Fact]
    public async Task Escribir_InvalidaLaCache()
    {
        var (cache, origen) = Crear();
        var ana = (await cache.BuscarPorEmailAsync("ana@test.com"))!;

        ana.Bloqueado = true;
        await cache.ActualizarAsync(ana);

        Assert.True((await cache.BuscarPorEmailAsync("ana@test.com"))!.Bloqueado);
        Assert.Equal(2, origen.Lecturas);
    }

    [Fact]
    public async Task ElBloqueoPorIntentosFunciona_AtravesDeLaCache()
    {
        var e = new Entorno();
        var cache = new CachedEmpleadosRepositorio(e.Empleados, new MemoryCache(new MemoryCacheOptions()));
        var login = new LoginService(cache, e.Permisos, e.Bitacora, e.Historial);

        await login.AutenticarAsync("ana@test.com", "0000");
        await login.AutenticarAsync("ana@test.com", "0000");
        var tercero = await login.AutenticarAsync("ana@test.com", "0000");

        Assert.Equal(LoginResultado.UsuarioBloqueado, tercero.Resultado);
        // y el PIN correcto ya no entra: la cache vio el bloqueo
        Assert.Equal(LoginResultado.UsuarioBloqueado, (await login.AutenticarAsync("ana@test.com", Entorno.PinAna)).Resultado);
    }
}

public class CachedFichajeStorageTests
{
    private class StorageContador : IFichajeStorageStrategy
    {
        public int LecturasDeTipo { get; private set; }
        public int LecturasDeLista { get; private set; }
        public string Tipo { get; set; } = "Salida";

        public Task GuardarAsync(FichajeDto fichaje) => Task.CompletedTask;
        public Task<string> ObtenerUltimoTipoFichajeAsync(string nombreCompleto) { LecturasDeTipo++; return Task.FromResult(Tipo); }
        public Task<List<FichajeDto>> ObtenerFichajesAsync() { LecturasDeLista++; return Task.FromResult(new List<FichajeDto>()); }
    }

    [Fact]
    public async Task ElUltimoTipo_SeCacheaHastaQueUnaCorreccionLoInvalida()
    {
        var origen = new StorageContador();
        var memoria = new MemoryCache(new MemoryCacheOptions());
        var storage = new CachedFichajeStorage(origen, memoria);

        Assert.Equal("Salida", await storage.ObtenerUltimoTipoFichajeAsync("Ana"));
        origen.Tipo = "Entrada"; // un administrador cargó una entrada a mano
        Assert.Equal("Salida", await storage.ObtenerUltimoTipoFichajeAsync("Ana")); // todavía cacheado
        Assert.Equal(1, origen.LecturasDeTipo);

        CachedFichajeStorage.InvalidarUltimoTipo(memoria, "Ana", DateTime.Today);

        Assert.Equal("Entrada", await storage.ObtenerUltimoTipoFichajeAsync("Ana"));
    }

    [Fact]
    public async Task GuardarUnFichaje_ActualizaElTipoYLaListaDelSupervisor()
    {
        var origen = new StorageContador();
        var storage = new CachedFichajeStorage(origen, new MemoryCache(new MemoryCacheOptions()));

        await storage.ObtenerFichajesAsync();
        await storage.ObtenerFichajesAsync();
        Assert.Equal(1, origen.LecturasDeLista);

        await storage.GuardarAsync(new FichajeDto { NombreCompleto = "Ana", FechaHora = DateTime.Now, TipoFichaje = "Entrada" });

        Assert.Equal("Entrada", await storage.ObtenerUltimoTipoFichajeAsync("Ana"));
        await storage.ObtenerFichajesAsync();
        Assert.Equal(2, origen.LecturasDeLista);
    }
}

public class BloqueoPorClaveTests
{
    [Fact]
    public async Task DosTareasConLaMismaClave_NoSePisan()
    {
        var bloqueo = new BloqueoPorClave();
        int dentro = 0, maximo = 0;

        async Task Seccion()
        {
            using var _ = await bloqueo.AdquirirAsync("Ana");
            int ahora = Interlocked.Increment(ref dentro);
            maximo = Math.Max(maximo, ahora);
            await Task.Delay(20);
            Interlocked.Decrement(ref dentro);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Seccion()));

        Assert.Equal(1, maximo);
    }

    [Fact]
    public async Task ClavesDistintas_SiCorrenEnParalelo()
    {
        var bloqueo = new BloqueoPorClave();
        int dentro = 0, maximo = 0;

        async Task Seccion(string clave)
        {
            using var _ = await bloqueo.AdquirirAsync(clave);
            int ahora = Interlocked.Increment(ref dentro);
            maximo = Math.Max(maximo, ahora);
            await Task.Delay(50);
            Interlocked.Decrement(ref dentro);
        }

        await Task.WhenAll(Seccion("Ana"), Seccion("Beto"), Seccion("Carla"));

        Assert.True(maximo > 1);
    }

    [Fact]
    public async Task LiberarDosVeces_NoRompeElLock()
    {
        var bloqueo = new BloqueoPorClave();
        var primero = await bloqueo.AdquirirAsync("Ana");
        primero.Dispose();
        primero.Dispose(); // el segundo Dispose no debe liberar un permiso que no tiene

        using var segundo = await bloqueo.AdquirirAsync("Ana");
        var tercero = bloqueo.AdquirirAsync("Ana");
        await Task.Delay(50);
        Assert.False(tercero.IsCompleted);
    }
}
