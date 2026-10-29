using FichajeApp.Api.Helpers;
using FichajeApp.Api.Models;
using FichajeApp.Api.Services;
using FichajeApp.Api.Validaciones;

namespace FichajeApp.Api.Tests;

public class GeoValidatorTests
{
    // Centro de la sede configurada en GeoValidator
    private const double Lat = -34.603722, Lon = -58.381592;

    [Fact]
    public void LaDistanciaAUnPuntoIgual_EsCero_YNoDependeDelOrden()
    {
        Assert.Equal(0, GeoValidator.DistanciaMetros(Lat, Lon, Lat, Lon), 3);
        Assert.Equal(GeoValidator.DistanciaMetros(Lat, Lon, -34.7, -58.5), GeoValidator.DistanciaMetros(-34.7, -58.5, Lat, Lon), 6);
    }

    [Fact]
    public void UnGradoDeLatitud_SonUnos111Kilometros() =>
        Assert.InRange(GeoValidator.DistanciaMetros(0, 0, 1, 0), 111_100, 111_300);

    [Theory]
    [InlineData(0.0, true)]       // en el centro
    [InlineData(0.0005, true)]    // ~55 m al norte
    [InlineData(0.002, false)]    // ~220 m: fuera del radio de 100 m
    [InlineData(0.05, false)]     // varios kilómetros
    public void EstaEnRango_SoloDentroDelRadioDeLaSede(double corrimientoDeLatitud, bool esperado) =>
        Assert.Equal(esperado, GeoValidator.EstaEnRango(Lat + corrimientoDeLatitud, Lon));
}

public class SitiosServiceTests
{
    private class SitiosFalsos : ISitiosRepository
    {
        public List<Sitio> Datos { get; } = new();
        public Task<IReadOnlyList<Sitio>> ObtenerSitiosAsync() => Task.FromResult<IReadOnlyList<Sitio>>(Datos);
    }

    private static SitiosService Con(params Sitio[] sitios)
    {
        var repo = new SitiosFalsos();
        repo.Datos.AddRange(sitios);
        return new SitiosService(repo);
    }

    [Fact]
    public async Task SinSitiosCargados_NoHayLugarConocido() =>
        Assert.Null(await Con().BuscarSitioAsync(-34.6, -58.4));

    [Fact]
    public async Task DevuelveElSitioQueContieneLaCoordenada()
    {
        var servicio = Con(new Sitio("Sucursal Centro", -34.6037, -58.3816, 100), new Sitio("Depósito", -34.7, -58.5, 100));

        Assert.Equal("Sucursal Centro", await servicio.BuscarSitioAsync(-34.6037, -58.3816));
        Assert.Equal("Depósito", await servicio.BuscarSitioAsync(-34.7001, -58.5001));
    }

    [Fact]
    public async Task FueraDelRadio_NoDevuelveNada() =>
        Assert.Null(await Con(new Sitio("Sucursal Centro", -34.6037, -58.3816, 50)).BuscarSitioAsync(-34.6, -58.3816)); // ~370 m

    [Fact]
    public async Task SiSeSuperponenVarios_GanaElMasCercano()
    {
        var servicio = Con(new Sitio("Grande", -34.6037, -58.3816, 5000), new Sitio("Chico", -34.6040, -58.3816, 500));

        Assert.Equal("Chico", await servicio.BuscarSitioAsync(-34.6040, -58.3816));
        Assert.Equal("Grande", await servicio.BuscarSitioAsync(-34.6037, -58.3816));
    }
}

public class ReglasDeValidacionTests
{
    private static FichajeContext Ctx(Action<FichajeContext>? ajustar = null)
    {
        var c = new FichajeContext
        {
            Empleado = new EmpleadoDto { Id = 1, NombreCompleto = "Ana", Email = "a@a.com", PinHash = "x", Activo = true },
            Latitud = -34.603722, Longitud = -58.381592, Precision = 10
        };
        ajustar?.Invoke(c);
        return c;
    }

    [Fact]
    public void EmpleadoActivo_RechazaSinEmpleado_OInactivo()
    {
        var regla = new ValidacionEmpleadoActivo();

        Assert.True(regla.Validar(Ctx()).EsValido);
        Assert.False(regla.Validar(Ctx(c => c.Empleado = null)).EsValido);
        Assert.False(regla.Validar(Ctx(c => c.Empleado!.Activo = false)).EsValido);
    }

    [Fact]
    public void UbicacionReal_RechazaLaSimulada_YLaPocoPrecisa_PeroNoLaQueNoTienePrecision()
    {
        var regla = new ValidacionUbicacionReal(50);

        Assert.Contains("simulada", regla.Validar(Ctx(c => c.EsUbicacionFalsa = true)).Mensaje);
        Assert.Contains("precisa", regla.Validar(Ctx(c => c.Precision = 80)).Mensaje);
        Assert.True(regla.Validar(Ctx(c => c.Precision = 50)).EsValido);   // justo en el límite
        Assert.True(regla.Validar(Ctx(c => c.Precision = null)).EsValido); // sin dato de precisión no se rechaza
    }

    [Fact]
    public void Motivo_SoloSeExigeFueraDeUnLugarConocido()
    {
        var regla = new ValidacionMotivoSiLugarDesconocido();

        var sin = regla.Validar(Ctx());
        Assert.False(sin.EsValido);
        Assert.Equal("MOTIVO_REQUERIDO", sin.Codigo);
        Assert.False(regla.Validar(Ctx(c => c.Motivo = "   ")).EsValido); // solo espacios no cuenta
        Assert.True(regla.Validar(Ctx(c => c.Motivo = "Visita al cliente")).EsValido);
        Assert.True(regla.Validar(Ctx(c => c.Sitio = "Sucursal Centro")).EsValido);
    }

    [Fact]
    public void SedeFija_AceptaSoloLaCoordenadaDeLaSede()
    {
        var regla = new ValidacionUbicacion();

        Assert.True(regla.Validar(Ctx()).EsValido);
        Assert.False(regla.Validar(Ctx(c => c.Latitud = -34.7)).EsValido);
    }

    [Fact]
    public void Foto_ObligatoriaPideBytes_NoAlcanzaConUnaListaVacia()
    {
        var regla = new ValidacionFoto();

        Assert.Equal("FOTO_REQUERIDA", regla.Validar(Ctx()).Codigo);
        Assert.False(regla.Validar(Ctx(c => c.Foto = Array.Empty<byte>())).EsValido);
        Assert.True(regla.Validar(Ctx(c => c.Foto = Imagenes.Jpeg)).EsValido);
    }

    private class ReglaEspia : IReglaValidacion
    {
        private readonly bool _ok;
        public int Llamadas { get; private set; }
        public ReglaEspia(bool ok) => _ok = ok;

        public ResultadoValidacion Validar(FichajeContext contexto)
        {
            Llamadas++;
            return _ok ? ResultadoValidacion.Ok() : ResultadoValidacion.Error("falló", "X");
        }
    }

    [Fact]
    public void Compuesta_SinReglasEsValida()
    {
        Assert.True(new ValidacionCompuesta().Validar(Ctx()).EsValido);
    }

    [Fact]
    public void Compuesta_CortaEnLaPrimeraQueFalla_YDevuelveSuMensaje()
    {
        var primera = new ReglaEspia(true);
        var segunda = new ReglaEspia(false);
        var tercera = new ReglaEspia(true);

        var r = new ValidacionCompuesta().Agregar(primera).Agregar(segunda).Agregar(tercera).Validar(Ctx());

        Assert.False(r.EsValido);
        Assert.Equal("X", r.Codigo);
        Assert.Equal(1, primera.Llamadas);
        Assert.Equal(1, segunda.Llamadas);
        Assert.Equal(0, tercera.Llamadas); // no se llega a evaluar
    }

    [Fact]
    public void Compuesta_PuedeAnidarOtraCompuesta()
    {
        var interna = new ValidacionCompuesta().Agregar(new ReglaEspia(false));
        var externa = new ValidacionCompuesta().Agregar(new ReglaEspia(true)).Agregar(interna);

        Assert.False(externa.Validar(Ctx()).EsValido);
    }
}

// Las mismas reglas, pero armadas según la configuración del cliente (lo que decide qué se valida)
public class ModosDeUbicacionTests
{
    private static FichajeContext Ctx(Entorno e, Action<FichajeContext>? ajustar = null)
    {
        var c = new FichajeContext
        {
            Empleado = e.Empleados.Datos.First(x => x.Email == "ana@test.com"),
            Latitud = -34.603722, Longitud = -58.381592, Precision = 10
        };
        ajustar?.Invoke(c);
        return c;
    }

    private static Task Modo(Entorno e, string ubicacion) =>
        e.ConfigFichaje.GuardarAsync("admin", new GuardarConfiguracionFichajeRequest { Ubicacion = ubicacion, Foto = "No" });

    [Fact]
    public async Task Libre_ExigeMotivoFueraDeUnSitio_YLoAceptaDentro()
    {
        var e = new Entorno();
        await Modo(e, "Libre");

        Assert.Equal("MOTIVO_REQUERIDO", (await e.Fichaje.ValidarAsync(Ctx(e))).Codigo);
        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, c => c.Sitio = "Sucursal Centro"))).EsValido);
        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e, c => c.Motivo = "Visita"))).EsValido);
    }

    [Fact]
    public async Task SedeFija_RechazaFueraDeLaSede_YNoPideMotivo()
    {
        var e = new Entorno();
        await Modo(e, "SedeFija");

        Assert.True((await e.Fichaje.ValidarAsync(Ctx(e))).EsValido);
        Assert.Contains("Fuera del rango", (await e.Fichaje.ValidarAsync(Ctx(e, c => c.Latitud = -34.7))).Mensaje);
    }

    [Fact]
    public async Task EnSedeFija_ElLugarQuedaComoSede_YEnLibreFueraDeUnSitioComoOtroLugar()
    {
        var e = new Entorno();

        await Modo(e, "SedeFija");
        Assert.Equal("Sede", (await e.Fichaje.RegistrarAsync(Ctx(e).Empleado!, Ctx(e))).Lugar);

        await Modo(e, "Libre");
        Assert.Equal("Otro lugar", (await e.Fichaje.RegistrarAsync(Ctx(e).Empleado!, Ctx(e, c => c.Motivo = "x"))).Lugar);
    }

    [Fact]
    public async Task UnEmpleadoInactivo_NoFichaEnNingunModo()
    {
        var e = new Entorno();
        foreach (var modo in new[] { "Ninguna", "Libre", "SedeFija" })
        {
            await Modo(e, modo);
            var r = await e.Fichaje.ValidarAsync(Ctx(e, c => { c.Empleado!.Activo = false; c.Sitio = "x"; }));
            Assert.False(r.EsValido, modo);
        }
    }

    [Fact]
    public async Task UnLugarFijo_SaltaLaUbicacion_PeroNoElEmpleadoActivo()
    {
        var e = new Entorno();
        await Modo(e, "SedeFija"); // en sede fija un teléfono lejos de la sede igual puede fichar si es kiosco

        var lejos = Ctx(e, c => { c.Latitud = -34.7; c.LugarFijo = "Sucursal Norte"; });
        Assert.True((await e.Fichaje.ValidarAsync(lejos)).EsValido);

        var inactivo = Ctx(e, c => { c.Empleado!.Activo = false; c.LugarFijo = "Sucursal Norte"; });
        Assert.False((await e.Fichaje.ValidarAsync(inactivo)).EsValido);
    }
}
