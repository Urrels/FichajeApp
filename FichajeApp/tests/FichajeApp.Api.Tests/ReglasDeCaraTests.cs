using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

public class ReglasDeCaraTests
{
    private static CaraDetectada Cara(float ancho) => new(ancho, ancho);

    [Fact]
    public void SinCaras_NoEsValida()
    {
        Assert.Equal(VeredictoCara.SinCara, ReglasDeCara.Evaluar(new List<CaraDetectada>()));
    }

    [Fact]
    public void UnaCaraDeCerca_EsValida()
    {
        Assert.Equal(VeredictoCara.Valida, ReglasDeCara.Evaluar(new[] { Cara(0.5f) }));
    }

    [Fact]
    public void UnaCaraMuyChica_EstaMuyLejos()
    {
        Assert.Equal(VeredictoCara.MuyLejos, ReglasDeCara.Evaluar(new[] { Cara(0.1f) }));
    }

    [Fact]
    public void VariasCaras_NoEsValida()
    {
        Assert.Equal(VeredictoCara.VariasCaras, ReglasDeCara.Evaluar(new[] { Cara(0.5f), Cara(0.4f) }));
    }

    [Theory]
    [InlineData(VeredictoCara.SinCara)]
    [InlineData(VeredictoCara.VariasCaras)]
    [InlineData(VeredictoCara.MuyLejos)]
    public void CadaRechazoTieneUnMensajeParaLaPersona(VeredictoCara v)
    {
        Assert.False(string.IsNullOrWhiteSpace(ReglasDeCara.Mensaje(v)));
    }

    [Fact]
    public void LaFotoValidaNoTieneMensaje()
    {
        Assert.Equal(string.Empty, ReglasDeCara.Mensaje(VeredictoCara.Valida));
    }
}
