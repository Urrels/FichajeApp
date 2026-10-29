using Microsoft.Extensions.Options;
using FichajeApp.Api.Models;
using FichajeApp.Api.Options;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Tests;

public class LimpiezaFotosTests
{
    private static LimpiezaFotos Limpieza(AlmacenFotosFalso fotos, RelojFalso reloj, int dias) =>
        new(fotos, reloj, Microsoft.Extensions.Options.Options.Create(new FichajeOptions { FotosRetencionDias = dias }));

    private static async Task Guardar(AlmacenFotosFalso fotos, params DateTime[] fechas)
    {
        foreach (var f in fechas) await fotos.GuardarAsync(Imagenes.Jpeg, f);
    }

    [Fact]
    public async Task BorraSoloLoQueSuperaLaRetencion()
    {
        var fotos = new AlmacenFotosFalso();
        var reloj = new RelojFalso { Ahora = new DateTime(2026, 11, 2, 12, 0, 0) };
        // Con 90 días, hoy se conserva desde el 4/8 (hace 90 días)
        await Guardar(fotos, new DateTime(2026, 8, 3), new DateTime(2026, 8, 4), new DateTime(2026, 10, 31), new DateTime(2026, 11, 2));

        var borradas = await Limpieza(fotos, reloj, 90).EjecutarAsync();

        Assert.Equal(1, borradas);
        Assert.Equal(3, fotos.Datos.Count);
        Assert.DoesNotContain(fotos.Datos.Keys, n => n.StartsWith("2026/08/03"));
    }

    [Fact]
    public async Task ConCeroDias_NoBorraNunca()
    {
        var fotos = new AlmacenFotosFalso();
        await Guardar(fotos, new DateTime(2020, 1, 1), new DateTime(2026, 11, 1));

        Assert.Equal(0, await Limpieza(fotos, new RelojFalso(), 0).EjecutarAsync());
        Assert.Equal(2, fotos.Datos.Count);
    }

    [Fact]
    public async Task SiNoHayNadaVencido_NoBorraNada_YSePuedeRepetir()
    {
        var fotos = new AlmacenFotosFalso();
        var reloj = new RelojFalso { Ahora = new DateTime(2026, 11, 2, 12, 0, 0) };
        await Guardar(fotos, new DateTime(2026, 11, 1));
        var limpieza = Limpieza(fotos, reloj, 30);

        Assert.Equal(0, await limpieza.EjecutarAsync());
        Assert.Equal(0, await limpieza.EjecutarAsync());
        Assert.Single(fotos.Datos);
    }

    [Fact]
    public async Task AlAvanzarElReloj_VanVenciendoLasQueFaltaban()
    {
        var fotos = new AlmacenFotosFalso();
        var reloj = new RelojFalso { Ahora = new DateTime(2026, 11, 2, 12, 0, 0) };
        await Guardar(fotos, new DateTime(2026, 10, 27));
        var limpieza = Limpieza(fotos, reloj, 7);

        Assert.Equal(0, await limpieza.EjecutarAsync()); // hoy 2/11: el límite es el 26/10 y la foto es del 27/10
        reloj.Ahora = reloj.Ahora.AddDays(-3);           // "hoy" es el 30/10: el límite pasa al 23/10
        Assert.Equal(0, await limpieza.EjecutarAsync());
        reloj.Ahora = reloj.Ahora.AddDays(10);           // "hoy" es el 9/11: el límite pasa al 2/11 y la foto ya venció
        Assert.Equal(1, await limpieza.EjecutarAsync());
    }
}

public class BorradoEnDiscoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "fichaje_borrado_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); }

    [Fact]
    public async Task BorraLosDiasAnterioresALaFecha_ConservaElResto_YQuitaLasCarpetasVacias()
    {
        var almacen = new AlmacenFotosEnDisco(_carpeta);
        var vieja1 = await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2026, 1, 10));
        var vieja2 = await almacen.GuardarAsync(Imagenes.Png, new DateTime(2026, 1, 10));
        var limite = await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2026, 3, 1));
        var nueva = await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2026, 3, 2));

        var borradas = await almacen.EliminarAnterioresAsync(new DateTime(2026, 3, 1));

        Assert.Equal(2, borradas);
        Assert.Null(await almacen.LeerAsync(vieja1));
        Assert.Null(await almacen.LeerAsync(vieja2));
        Assert.NotNull(await almacen.LeerAsync(limite)); // la fecha límite en sí se conserva
        Assert.NotNull(await almacen.LeerAsync(nueva));
        Assert.False(Directory.Exists(Path.Combine(_carpeta, "2026", "01"))); // sin carpetas huérfanas
    }

    [Fact]
    public async Task SiSeBorraTodoUnAnio_TampocoQuedaSuCarpeta()
    {
        var almacen = new AlmacenFotosEnDisco(_carpeta);
        await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2025, 6, 1));

        Assert.Equal(1, await almacen.EliminarAnterioresAsync(new DateTime(2026, 1, 1)));
        Assert.False(Directory.Exists(Path.Combine(_carpeta, "2025")));
    }

    [Fact]
    public async Task NoTocaCarpetasQueNoCreoElAlmacen()
    {
        var almacen = new AlmacenFotosEnDisco(_carpeta);
        await almacen.GuardarAsync(Imagenes.Jpeg, new DateTime(2020, 1, 1));
        var ajena = Path.Combine(_carpeta, "copias");
        Directory.CreateDirectory(ajena);
        File.WriteAllText(Path.Combine(ajena, "notas.txt"), "no borrar");
        var anioRaro = Path.Combine(_carpeta, "20x6", "01", "01"); // no es un año
        Directory.CreateDirectory(anioRaro);

        await almacen.EliminarAnterioresAsync(new DateTime(2026, 1, 1));

        Assert.True(File.Exists(Path.Combine(ajena, "notas.txt")));
        Assert.True(Directory.Exists(anioRaro));
    }

    [Fact]
    public async Task SinCarpetaDeFotos_NoFalla()
    {
        var almacen = new AlmacenFotosEnDisco(Path.Combine(_carpeta, "no-existe"));

        Assert.Equal(0, await almacen.EliminarAnterioresAsync(DateTime.Today));
    }
}

public class RetencionEnLaConfiguracionTests
{
    [Fact]
    public async Task LaConfiguracionInformaCuantoSeGuardanLasFotos_YElValorLoDefineElServidor()
    {
        var e = new Entorno();

        Assert.Equal(90, (await e.ConfigFichaje.ObtenerAsync()).RetencionFotosDias); // valor por defecto

        var otro = new ConfiguracionFichajeService(e.ConfigFichajeRepo, e.Bitacora,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new FichajeOptions { FotosRetencionDias = 30 }));
        Assert.Equal(30, (await otro.ObtenerAsync()).RetencionFotosDias);

        // Guardar la configuración desde la app no cambia la retención
        await otro.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = "Libre", Foto = "Opcional" });
        Assert.Equal(30, (await otro.ObtenerAsync()).RetencionFotosDias);
    }
}
