using FichajeApp.Api.Infrastructure;

namespace FichajeApp.Api.Tests;

// Lo que devuelve la API de Google no es lo que uno supone: un entero llega como long y un número con
// decimales como double. Un bug así se escapó porque los repositorios en memoria no pasan por acá.
public class LecturaDeCeldasTests
{
    private static FilaHoja Fila(params object[] valores) => new(2, valores);

    [Fact]
    public void UnEnteroQueLlegaComoLong_NoSeLeeComoCero()
    {
        var fila = Fila(7L, "Ana", 3L);

        Assert.Equal(7, fila.Entero(0));
        Assert.Equal(3, fila.Entero(2));
        Assert.Equal(7m, fila.Decimal(0));
    }

    [Theory]
    [MemberData(nameof(TiposNumericos))]
    public void CualquierTipoNumerico_SeLee(object valor)
    {
        Assert.Equal(5, Fila(valor).Entero(0));
    }

    public static IEnumerable<object[]> TiposNumericos() => new[]
    {
        new object[] { 5L }, new object[] { 5 }, new object[] { 5.0 }, new object[] { 5m }, new object[] { (short)5 }, new object[] { 5f }
    };

    [Fact]
    public void LosTextosConComaDecimal_SeLeen()
    {
        Assert.Equal(1500.5m, Fila("1500,5").Decimal(0));
        Assert.Equal(1500.5m, Fila("1500.5").Decimal(0));
    }

    [Fact]
    public void UnaCeldaVaciaOInvalida_ValeCero()
    {
        Assert.Equal(0, Fila().Entero(0));
        Assert.Equal(0, Fila("").Entero(0));
        Assert.Equal(0, Fila("abc").Entero(0));
        Assert.Null(Fila("").EnteroONulo(0));
    }

    [Fact]
    public void LosBooleanos_SeLeenComoBooleanoTextoONumero()
    {
        Assert.True(Fila(true).Booleano(0));
        Assert.True(Fila("TRUE").Booleano(0));
        Assert.True(Fila(1L).Booleano(0));
        Assert.False(Fila(0L).Booleano(0));
        Assert.False(Fila(false).Booleano(0));
        Assert.False(Fila().Booleano(0));
    }

    [Fact]
    public void LasFechasAceptanTextoYNumeroDeSerie_EnteroODecimal()
    {
        // 2026-10-07 a las 00:00 es un número de serie ENTERO (llega como long)
        var medianoche = (long)(new DateTime(2026, 10, 7) - new DateTime(1899, 12, 30)).TotalDays;
        Assert.Equal(new DateTime(2026, 10, 7), Fila(medianoche).Fecha(0));

        // 2026-10-07 08:30:00 lleva fracción (llega como double)
        var conHora = (new DateTime(2026, 10, 7, 8, 30, 0) - new DateTime(1899, 12, 30)).TotalDays;
        Assert.Equal(new DateTime(2026, 10, 7, 8, 30, 0), Fila(conHora).Fecha(0));

        Assert.Equal(new DateTime(2026, 10, 7, 8, 30, 0), Fila("2026-10-07 08:30:00").Fecha(0));
        Assert.Null(Fila("no es fecha").FechaONula(0));
        Assert.Null(Fila().FechaONula(0));
    }

    [Fact]
    public void ElNumeroDeSerie_SeRedondeaAlSegundoMasCercano()
    {
        var casi = (new DateTime(2026, 10, 7, 8, 30, 0) - new DateTime(1899, 12, 30)).TotalDays + 0.000000001;
        Assert.Equal(new DateTime(2026, 10, 7, 8, 30, 0), Fila(casi).Fecha(0));
    }
}
