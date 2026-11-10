using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class RecorridoRepoFalso : IRecorridoRepositorio
{
    public List<(int EmpleadoId, PuntoGuardado Punto)> Datos { get; } = new();

    public Task AgregarAsync(int empleadoId, IReadOnlyCollection<PuntoGuardado> puntos)
    {
        Datos.AddRange(puntos.Select(p => (empleadoId, p)));
        return Task.CompletedTask;
    }

    public Task<List<PuntoGuardado>> ListarAsync(int empleadoId, DateTime desde, DateTime hasta) =>
        Task.FromResult(Datos.Where(d => d.EmpleadoId == empleadoId && d.Punto.FechaHora >= desde && d.Punto.FechaHora < hasta)
            .Select(d => d.Punto).OrderBy(p => p.FechaHora).ToList());

    public Task<int> EliminarAnterioresAsync(DateTime antesDe) => Task.FromResult(Datos.RemoveAll(d => d.Punto.FechaHora < antesDe));
}

public class RecorridoTests
{
    private readonly Entorno _e = new();
    private readonly RecorridoRepoFalso _repo = new();
    private readonly RecorridoService _servicio;

    public RecorridoTests() => _servicio = new RecorridoService(_repo, _e.Fichaje, _e.Reloj);

    private EmpleadoDto Ana => _e.Empleados.Datos.Single(x => x.Email == "ana@test.com");

    private PuntoRecorrido Punto(int minutosAtras = 1, double lat = -34.6, double lon = -58.4, double? precision = 20) => new()
    {
        FechaHora = new DateTimeOffset(DateTime.SpecifyKind(_e.Reloj.Ahora.AddMinutes(-minutosAtras), DateTimeKind.Utc)),
        Latitud = lat, Longitud = lon, Precision = precision
    };

    private async Task ActivarRecorrido(bool si) =>
        await _e.ConfigFichaje.GuardarAsync("admin@test.com", new GuardarConfiguracionFichajeRequest
        {
            Ubicacion = "Ninguna", Foto = "No", SeguimientoRecorrido = si
        });

    private async Task AbrirJornada() =>
        await _e.Fichaje.RegistrarAsync(Ana, new FichajeContext { Empleado = Ana, Latitud = -34.6, Longitud = -58.4, Precision = 10 });

    [Fact]
    public async Task ConLaOpcionApagada_NoSeGuardaNadaYSeLeDiceAlTelefonoQuePare()
    {
        await ActivarRecorrido(false);
        await AbrirJornada();

        var r = await _servicio.RegistrarAsync(Ana, new[] { Punto() });

        Assert.False(r.Seguir);
        Assert.Equal(0, r.Aceptados);
        Assert.Empty(_repo.Datos);
    }

    [Fact]
    public async Task SinJornadaAbierta_NoSeGuardaNada()
    {
        await ActivarRecorrido(true);

        var r = await _servicio.RegistrarAsync(Ana, new[] { Punto() });

        Assert.False(r.Seguir);
        Assert.Empty(_repo.Datos);
    }

    [Fact]
    public async Task ConJornadaAbiertaYLaOpcionActiva_SeGuardanLosPuntos()
    {
        await ActivarRecorrido(true);
        await AbrirJornada();

        var r = await _servicio.RegistrarAsync(Ana, new[] { Punto(10), Punto(5) });

        Assert.True(r.Seguir);
        Assert.Equal(2, r.Aceptados);
        Assert.Equal(2, _repo.Datos.Count);
    }

    [Fact]
    public async Task DespuesDeLaSalida_ElTelefonoRecibeQuePare()
    {
        await ActivarRecorrido(true);
        await AbrirJornada();
        await _e.Fichaje.RegistrarAsync(Ana, new FichajeContext { Empleado = Ana, Latitud = -34.6, Longitud = -58.4, Precision = 10 });   // Salida

        var r = await _servicio.RegistrarAsync(Ana, new[] { Punto() });

        Assert.False(r.Seguir);
        Assert.Empty(_repo.Datos);
    }

    [Fact]
    public async Task SeDescartanLosPuntosInvalidos()
    {
        await ActivarRecorrido(true);
        await AbrirJornada();

        var r = await _servicio.RegistrarAsync(Ana, new[]
        {
            Punto(),                          // bueno
            Punto(lat: 0, lon: 0),            // 0,0: sin señal
            Punto(lat: 123),                  // latitud imposible
            Punto(lat: double.NaN),
            Punto(precision: 900),            // GPS muy impreciso
            Punto(minutosAtras: 60 * 30),     // de hace más de un día
            Punto(minutosAtras: -30)          // del futuro
        });

        Assert.Equal(1, r.Aceptados);
        Assert.Single(_repo.Datos);
    }

    [Fact]
    public async Task NoSeAceptanMasDeCienPuntosPorEnvio()
    {
        await ActivarRecorrido(true);
        await AbrirJornada();

        var r = await _servicio.RegistrarAsync(Ana, Enumerable.Range(1, 150).Select(i => Punto(i % 50 + 1)).ToList());

        Assert.Equal(RecorridoService.MaxPuntosPorEnvio, r.Aceptados);
    }

    [Fact]
    public async Task ElSupervisorVeSoloLosPuntosDeEseDia()
    {
        await ActivarRecorrido(true);
        await AbrirJornada();
        await _servicio.RegistrarAsync(Ana, new[] { Punto(5) });

        var hoy = await _servicio.ObtenerAsync(Ana.Id, _e.Reloj.Ahora.Date);
        var ayer = await _servicio.ObtenerAsync(Ana.Id, _e.Reloj.Ahora.Date.AddDays(-1));

        Assert.Single(hoy);
        Assert.Empty(ayer);
    }

    [Fact]
    public async Task LaOpcionDeRecorridoSeConservaAlGuardarOtrosAjustes()
    {
        await ActivarRecorrido(true);

        // Guardar solo la foto no apaga el recorrido (el campo no viene: se conserva)
        await _e.ConfigFichaje.GuardarAsync("admin@test.com", new GuardarConfiguracionFichajeRequest { Ubicacion = "Ninguna", Foto = "Opcional" });

        Assert.True((await _e.ConfigFichaje.ObtenerAsync()).SeguimientoRecorrido);
    }

    [Fact]
    public async Task ElRecorridoVieneApagadoDeFabrica()
    {
        Assert.False((await _e.ConfigFichaje.ObtenerAsync()).SeguimientoRecorrido);
    }
}
