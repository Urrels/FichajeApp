using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

public class ColorHsvTests
{
    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#FFFFFF", 0, 0, 1)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#808080", 0, 0, 0.502)]
    public void Convierte_de_hex_a_tono_saturacion_y_brillo(string hex, double h, double s, double v)
    {
        var (th, ts, tv) = ColorHsv.Desde(hex);

        Assert.Equal(h, th, 1);
        Assert.Equal(s, ts, 2);
        Assert.Equal(v, tv, 2);
    }

    [Theory]
    [InlineData(0, 1, 1, "#FF0000")]
    [InlineData(60, 1, 1, "#FFFF00")]
    [InlineData(120, 1, 1, "#00FF00")]
    [InlineData(180, 1, 1, "#00FFFF")]
    [InlineData(300, 1, 1, "#FF00FF")]
    [InlineData(200, 0, 1, "#FFFFFF")]
    [InlineData(200, 1, 0, "#000000")]
    public void Convierte_de_tono_saturacion_y_brillo_a_hex(double h, double s, double v, string esperado) =>
        Assert.Equal(esperado, ColorHsv.AHex(h, s, v));

    [Theory]
    [InlineData("#1B5E9B")]
    [InlineData("#455A64")]
    [InlineData("#FFB300")]
    [InlineData("#C62828")]
    [InlineData("#00796B")]
    [InlineData("#123456")]
    public void Ida_y_vuelta_no_cambia_el_color(string hex)
    {
        var (h, s, v) = ColorHsv.Desde(hex);

        Assert.Equal(hex, ColorHsv.AHex(h, s, v));
    }

    [Fact]
    public void Todos_los_colores_posibles_hacen_ida_y_vuelta_en_una_muestra_grande()
    {
        var azar = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var hex = $"#{azar.Next(256):X2}{azar.Next(256):X2}{azar.Next(256):X2}";
            var (h, s, v) = ColorHsv.Desde(hex);
            Assert.Equal(hex, ColorHsv.AHex(h, s, v));
        }
    }

    [Fact]
    public void Los_valores_fuera_de_rango_se_acotan_y_el_tono_da_la_vuelta()
    {
        Assert.Equal("#FF0000", ColorHsv.AHex(360, 1, 1));
        Assert.Equal(ColorHsv.AHex(10, 1, 1), ColorHsv.AHex(370, 1, 1));
        Assert.Equal(ColorHsv.AHex(350, 1, 1), ColorHsv.AHex(-10, 1, 1));
        Assert.Equal("#FFFFFF", ColorHsv.AHex(0, -5, 3));
    }

    [Fact]
    public void El_contraste_coincide_con_los_valores_conocidos()
    {
        Assert.Equal(21, ColorHsv.Contraste("#000000", "#FFFFFF"), 1);
        Assert.Equal(1, ColorHsv.Contraste("#336699", "#336699"), 3);
        Assert.Equal(ColorHsv.Contraste("#1B5E9B", "#FFFFFF"), ColorHsv.Contraste("#FFFFFF", "#1B5E9B"), 6);
    }

    [Theory]
    [InlineData("#1B5E9B", "#FFFFFF")]   // fondo oscuro: texto blanco
    [InlineData("#455A64", "#FFFFFF")]
    [InlineData("#FFB300", "#212121")]   // fondo claro: texto oscuro
    [InlineData("#FDD835", "#212121")]
    [InlineData("#FFFFFF", "#212121")]
    [InlineData("#000000", "#FFFFFF")]
    public void El_texto_legible_es_el_que_mejor_contrasta(string fondo, string esperado) =>
        Assert.Equal(esperado, ColorHsv.TextoLegible(fondo));

    [Fact]
    public void El_texto_legible_siempre_cumple_el_contraste_minimo_que_pide_la_app()
    {
        var azar = new Random(11);
        for (int i = 0; i < 2000; i++)
        {
            var fondo = $"#{azar.Next(256):X2}{azar.Next(256):X2}{azar.Next(256):X2}";
            Assert.True(ColorHsv.Contraste(fondo, ColorHsv.TextoLegible(fondo)) >= 3.0, $"No se lee sobre {fondo}");
        }
    }
}
