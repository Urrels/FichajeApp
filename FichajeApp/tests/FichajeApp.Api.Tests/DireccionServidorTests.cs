using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

// La dirección que se escribe a mano en el teléfono (ajuste oculto del login)
public class DireccionServidorTests
{
    [Theory]
    [InlineData("https://algo.trycloudflare.com", "https://algo.trycloudflare.com/api")]
    [InlineData("https://algo.trycloudflare.com/", "https://algo.trycloudflare.com/api")]
    [InlineData("https://algo.trycloudflare.com/api", "https://algo.trycloudflare.com/api")]
    [InlineData("https://algo.trycloudflare.com/api/", "https://algo.trycloudflare.com/api")]
    [InlineData("  https://algo.trycloudflare.com  ", "https://algo.trycloudflare.com/api")]
    [InlineData("algo.trycloudflare.com", "https://algo.trycloudflare.com/api")]                 // sin esquema: se asume https
    [InlineData("HTTPS://Algo.TryCloudflare.COM/API", "https://algo.trycloudflare.com/API")]     // el host se normaliza
    [InlineData("https://mi-servidor.com:8443", "https://mi-servidor.com:8443/api")]             // conserva el puerto
    [InlineData("https://mi-servidor.com/fichaje", "https://mi-servidor.com/fichaje/api")]       // API detrás de una subruta
    public void AceptaYDejaLaDireccionCompleta(string entrada, string esperada)
    {
        var (valida, url, mensaje) = DireccionServidor.Normalizar(entrada);

        Assert.True(valida, mensaje);
        Assert.Equal(esperada, url);
    }

    [Theory]
    [InlineData("http://10.0.2.2:5224", "http://10.0.2.2:5224/api")]
    [InlineData("http://localhost:5224/api", "http://localhost:5224/api")]
    public void Http_SoloParaElEmuladorYLaPropiaPc(string entrada, string esperada)
    {
        var (valida, url, _) = DireccionServidor.Normalizar(entrada);

        Assert.True(valida);
        Assert.Equal(esperada, url);
    }

    [Theory]
    [InlineData("http://192.168.1.20:5224")]   // red local: Android la bloquea
    [InlineData("http://mi-servidor.com")]
    public void Http_FueraDelEmuladorSeRechaza_ConUnMensajeQueLoExplica(string entrada)
    {
        var (valida, _, mensaje) = DireccionServidor.Normalizar(entrada);

        Assert.False(valida);
        Assert.Contains("https", mensaje);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://servidor.com")]
    [InlineData("https://")]
    [InlineData("https://usuario:clave@servidor.com")]   // no se aceptan credenciales en la dirección
    [InlineData("https://servidor.com/api?x=1")]
    [InlineData("https://servidor.com/api#algo")]
    [InlineData("javascript:alert(1)")]
    public void RechazaLoQueNoEsUnaDireccionDeServidor(string? entrada)
    {
        var (valida, url, mensaje) = DireccionServidor.Normalizar(entrada);

        Assert.False(valida);
        Assert.Equal(string.Empty, url);
        Assert.NotEmpty(mensaje);
    }

    [Fact]
    public void LoQueDevuelve_SePuedeVolverANormalizarSinCambios()
    {
        var (_, primera, _) = DireccionServidor.Normalizar("algo.trycloudflare.com");
        var (_, segunda, _) = DireccionServidor.Normalizar(primera);

        Assert.Equal(primera, segunda);
    }
}
