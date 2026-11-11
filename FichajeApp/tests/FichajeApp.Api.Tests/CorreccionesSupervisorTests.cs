using FichajeApp.Api.Models;
using FichajeApp.Api.Services;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

public class CorreccionesSupervisorTests
{
    // ---------- Fichaje abierto que el administrador corrige ----------

    [Fact]
    public async Task UnaEntradaAbierta_ApareceComoPresente()
    {
        var e = new Entorno();
        var hoy = DateTime.Now.Date;
        var entrada = new FichajeDto { NombreCompleto = "Ana Empleada", TipoFichaje = "Entrada", FechaHora = hoy.AddMinutes(1) };
        e.StorageFichajes.Guardados.Add(entrada);
        e.Fichajes.Agregar("Ana Empleada", "Entrada", entrada.FechaHora);

        var consulta = new ConsultaFichajesService(e.StorageFichajes, e.Fichajes);
        Assert.Single(await consulta.ObtenerPresentesAsync());
    }

    [Fact]
    public async Task SiElAdministradorCargaLaSalida_YaNoEstaPresente()
    {
        var e = new Entorno();
        var hoy = DateTime.Now.Date;
        var entrada = new FichajeDto { NombreCompleto = "Ana Empleada", TipoFichaje = "Entrada", FechaHora = hoy.AddMinutes(1) };
        e.StorageFichajes.Guardados.Add(entrada);
        e.Fichajes.Agregar("Ana Empleada", "Entrada", entrada.FechaHora);

        // La corrección del administrador agrega una salida "manual": no está en el almacén de fichajes reales
        await e.Fichajes.AgregarManualAsync("Ana Empleada", "Salida", entrada.FechaHora.AddMinutes(5), "Se olvidó de fichar", "admin", DateTime.Now);

        var consulta = new ConsultaFichajesService(e.StorageFichajes, e.Fichajes);
        Assert.Empty(await consulta.ObtenerPresentesAsync());
    }

    [Fact]
    public async Task LaSalidaDeOtraPersona_NoCierraLaEntradaDeAna()
    {
        var e = new Entorno();
        var hoy = DateTime.Now.Date;
        var entrada = new FichajeDto { NombreCompleto = "Ana Empleada", TipoFichaje = "Entrada", FechaHora = hoy.AddMinutes(1) };
        e.StorageFichajes.Guardados.Add(entrada);
        e.Fichajes.Agregar("Ana Empleada", "Entrada", entrada.FechaHora);
        await e.Fichajes.AgregarManualAsync("Beto Supervisor", "Salida", entrada.FechaHora.AddMinutes(5), "x", "admin", DateTime.Now);

        var consulta = new ConsultaFichajesService(e.StorageFichajes, e.Fichajes);
        Assert.Single(await consulta.ObtenerPresentesAsync());
    }

    [Fact]
    public async Task ElAdministrador_AlCorregirUnaEntradaAbierta_LaJornadaQuedaCompleta()
    {
        var e = new Entorno();
        var ayer = e.Reloj.Ahora.Date.AddDays(-1);
        e.Fichajes.Agregar("Ana Empleada", "Entrada", ayer.AddHours(8));
        var id = e.Fichajes.Datos[0].Id;

        var antes = await e.FichajesAdmin.ListarIncompletosAsync(ayer, ayer, soloVencidos: false);
        Assert.Single(antes);

        await e.FichajesAdmin.CorregirAsync("admin@test.com", id, ayer.AddHours(8), ayer.AddHours(16), "Olvidó fichar la salida");

        Assert.Empty(await e.FichajesAdmin.ListarIncompletosAsync(ayer, ayer, soloVencidos: false));
    }

    [Fact]
    public async Task EnSql_UnaSalidaCargadaPorElAdministrador_CierraLaEntradaAbierta()
    {
        using var bd = new BaseSqlite();
        var guardar = new FichajeApp.Api.Persistencia.Sql.FichajesSqlStorage(bd);
        var repo = new FichajeApp.Api.Persistencia.Sql.FichajesSqlRepositorio(bd,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var hoy = DateTime.Now.Date;

        await guardar.GuardarAsync(new FichajeDto { NombreCompleto = "Ana", TipoFichaje = "Entrada", FechaHora = hoy.AddMinutes(1) });
        Assert.Equal("Entrada", await guardar.ObtenerUltimoTipoFichajeAsync("Ana"));   // abierta: ahora toca la salida

        await repo.AgregarManualAsync("Ana", "Salida", hoy.AddMinutes(2), "Olvidó fichar", "admin@x.com", hoy.AddMinutes(3));

        // Cerrada: lo próximo es una entrada nueva
        Assert.Equal("Salida", await guardar.ObtenerUltimoTipoFichajeAsync("Ana"));
    }

    // ---------- Google Maps ----------

    [Fact]
    public void ElEnlaceDeMapas_LlevaLasCoordenadasExactasConPuntoDecimal()
    {
        var url = UrlDeMapas.Google(-34.603722, -58.381592);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=-34.603722,-58.381592", url);
    }

    // ---------- Control de cara ----------

    [Fact]
    public void UnaCaraSinOjosNiNariz_NoEsUnaCara()
    {
        Assert.Equal(VeredictoCara.SinCara, ReglasDeCara.Evaluar(new[] { new CaraDetectada(0.6f, 0.6f, TieneRasgos: false) }));
    }

    [Fact]
    public void UnaCaraConRasgosYDeCerca_Pasa()
    {
        Assert.Equal(VeredictoCara.Valida, ReglasDeCara.Evaluar(new[] { new CaraDetectada(0.6f, 0.6f, TieneRasgos: true) }));
    }
}
