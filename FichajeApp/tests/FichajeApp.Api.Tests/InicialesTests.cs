using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

public class InicialesTests
{
    [Theory]
    [InlineData("Ana Empleada", "AE")]
    [InlineData("admin visual", "AV")]
    [InlineData("Administrador 🔒", "A")]      // el adorno no es una inicial
    [InlineData("🔒 Supervisor", "S")]
    [InlineData("Ana María López", "AM")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Las_iniciales_son_solo_letras_o_numeros(string? texto, string esperado) =>
        Assert.Equal(esperado, Iniciales.De(texto));
}
