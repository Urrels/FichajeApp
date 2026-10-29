using Microsoft.Extensions.Configuration;
using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Tests;

public class ConfiguracionSeguraTests
{
    private static IConfiguration Con(string? secreto) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secreto }).Build();

    private const string Propio = "esta-es-una-clave-propia-de-mas-de-32-caracteres!!";

    [Fact]
    public void EnProduccion_ConUnaClavePropia_Arranca() =>
        ConfiguracionSegura.ExigirSecretoJwt(Con(Propio), esDesarrollo: false);

    [Fact]
    public void EnProduccion_ConLaClaveDeEjemplo_NoArranca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionSegura.ExigirSecretoJwt(Con(ConfiguracionSegura.SecretoDeEjemplo), esDesarrollo: false));

        Assert.Contains("Jwt__Secret", ex.Message);
    }

    [Fact]
    public void EnDesarrolloYPruebas_LaClaveDeEjemploSeAcepta() =>
        ConfiguracionSegura.ExigirSecretoJwt(Con(ConfiguracionSegura.SecretoDeEjemplo), esDesarrollo: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinClave_NuncaArranca(string? secreto)
    {
        Assert.Throws<InvalidOperationException>(() => ConfiguracionSegura.ExigirSecretoJwt(Con(secreto), esDesarrollo: true));
        Assert.Throws<InvalidOperationException>(() => ConfiguracionSegura.ExigirSecretoJwt(Con(secreto), esDesarrollo: false));
    }

    [Fact]
    public void UnaClaveCorta_NuncaArranca_PorqueFirmarConEllaEsDebil()
    {
        Assert.Throws<InvalidOperationException>(() => ConfiguracionSegura.ExigirSecretoJwt(Con("corta"), esDesarrollo: true));
        Assert.Throws<InvalidOperationException>(() => ConfiguracionSegura.ExigirSecretoJwt(Con(new string('x', 31)), esDesarrollo: false));
        ConfiguracionSegura.ExigirSecretoJwt(Con(new string('x', 32)), esDesarrollo: false); // justo en el mínimo
    }

    [Fact]
    public void LaClaveDeEjemplo_CumpleElLargoMinimo_ParaQueDesarrolloFuncione() =>
        Assert.True(ConfiguracionSegura.SecretoDeEjemplo.Length >= ConfiguracionSegura.LargoMinimo);
}
