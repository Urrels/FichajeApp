using System.Net.Sockets;
using System.Reflection;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

// Lo que la persona lee cuando algo sale mal: claro, amable y sin nada técnico
public class TextosDeErrorTests
{
    private static IEnumerable<string> TodosLosTextos() => typeof(TextosDeError)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void LosTextosFijos_SonCortosAmablesYSinJerga()
    {
        var textos = TodosLosTextos().ToList();
        Assert.True(textos.Count >= 8);

        foreach (var texto in textos)
        {
            Assert.False(string.IsNullOrWhiteSpace(texto));
            Assert.InRange(texto.Length, 20, 140);                  // hasta dos líneas en un teléfono
            Assert.EndsWith(".", texto);                             // frases completas
            Assert.DoesNotContain("Exception", texto);
            Assert.DoesNotContain("HTTP", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("null", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Error de conexión:", texto);      // el formato viejo, que pegaba el texto técnico
            Assert.DoesNotContain("{", texto);                       // sin marcas de formato sin resolver
        }
    }

    [Fact]
    public void LosTextos_HablanDeVos()
    {
        // Voseo rioplatense: "probá", "revisá", "entrá"; nunca "prueba", "revisa" en imperativo de tú
        var junto = string.Join(" ", TodosLosTextos());
        Assert.Contains("Probá", junto);
        Assert.Contains("Revisá", junto);
        Assert.DoesNotContain("Intenta ", junto);
        Assert.DoesNotContain("Verifica ", junto);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), TextosDeError.SinConexion)]
    [InlineData(typeof(SocketException), TextosDeError.SinConexion)]
    [InlineData(typeof(IOException), TextosDeError.SinConexion)]
    [InlineData(typeof(TaskCanceledException), TextosDeError.TiempoAgotado)]
    [InlineData(typeof(OperationCanceledException), TextosDeError.TiempoAgotado)]
    [InlineData(typeof(TimeoutException), TextosDeError.TiempoAgotado)]
    [InlineData(typeof(InvalidOperationException), TextosDeError.ErrorGenerico)]
    [InlineData(typeof(NullReferenceException), TextosDeError.ErrorGenerico)]
    public void UnaExcepcion_SeTraduceSegunSuTipo(Type tipo, string esperado)
    {
        var ex = (Exception)Activator.CreateInstance(tipo)!;

        Assert.Equal(esperado, TextosDeError.DeExcepcion(ex));
    }

    [Fact]
    public void ElTextoTecnicoDeLaExcepcion_NuncaLlegaALaPantalla()
    {
        var ex = new HttpRequestException("Unable to resolve host 'api.ejemplo.com' (nodename nor servname provided)");

        var texto = TextosDeError.DeExcepcion(ex);

        Assert.DoesNotContain("resolve", texto);
        Assert.DoesNotContain("api.ejemplo.com", texto);
    }

    [Theory]
    [InlineData("PIN_INCORRECTO", "Ese PIN no coincide. Probá de nuevo.")]
    [InlineData("FOTO_REQUERIDA", "Para fichar necesitamos una foto. Sacate una y listo.")]
    public void LosCodigosDelServidor_SeTraducenAFrasesAmables(string codigo, string esperado) =>
        Assert.Equal(esperado, TextosDeError.Para(codigo, "mensaje técnico del servidor"));

    [Theory]
    [InlineData("BLOQUEADO")]
    [InlineData("KIOSCO_DESACTIVADO")]
    [InlineData("FOTO_INVALIDA")]
    [InlineData("SIN_PERMISO")]
    public void CadaCodigoConocido_TieneSuPropioTexto_YNoUsaElDelServidor(string codigo)
    {
        var texto = TextosDeError.Para(codigo, "TEXTO DEL SERVIDOR");

        Assert.NotEqual("TEXTO DEL SERVIDOR", texto);
        Assert.NotEqual(TextosDeError.ErrorGenerico, texto);
    }

    [Fact]
    public void SinUnCodigoConocido_SeUsaElMensajeDelServidor_QueYaEstaEscritoParaPersonas()
    {
        Assert.Equal("Ya existe un usuario con ese email.", TextosDeError.Para(null, "Ya existe un usuario con ese email."));
        Assert.Equal("Algo", TextosDeError.Para("CODIGO_NUEVO_QUE_NO_CONOCEMOS", "Algo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinCodigoNiMensaje_SeUsaUnaFraseGenerica_NuncaUnTextoVacio(string? mensaje) =>
        Assert.Equal(TextosDeError.ErrorGenerico, TextosDeError.Para(null, mensaje));

    [Theory]
    [InlineData("Entrada", 8, 2, "¡Listo! Entrada registrada a las 08:02.")]
    [InlineData("Salida", 17, 45, "¡Listo! Salida registrada a las 17:45.")]
    public void ElFichajeExitoso_DiceQueSeRegistroYLaHora(string tipo, int hora, int minuto, string esperado) =>
        Assert.Equal(esperado, TextosDeError.FichajeRegistrado(tipo, new DateTime(2026, 10, 30, hora, minuto, 0)));

    [Fact]
    public void LaSeveridadTieneLosCuatroEstadosDelSistemaDeFeedback() =>
        Assert.Equal(new[] { "Info", "Exito", "Aviso", "Error" }, Enum.GetNames<Severidad>());
}
