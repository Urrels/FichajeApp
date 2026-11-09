using FichajeApp.Mobile.Services;

namespace FichajeApp.Api.Tests;

// Qué botón ofrece el cartel de error en cada caso que puede pasar al fichar
public class AccionDeErrorTests
{
    [Theory]
    [InlineData("Necesitamos el permiso de ubicación para fichar. Habilitalo en Ajustes → Apps → Fichaje → Permisos.")]
    [InlineData("Permiso de ubicación denegado")]
    [InlineData("El GPS está apagado. Activá la ubicación del teléfono y volvé a intentar.")]
    [InlineData("No se pudo obtener la ubicación. Activá el GPS del teléfono (modo de alta precisión) y probá al aire libre.")]
    [InlineData("No pudimos usar la cámara. Revisá el permiso en Ajustes y probá de nuevo.")]
    public void Los_problemas_de_permisos_y_GPS_ofrecen_abrir_ajustes(string texto) =>
        Assert.Equal(AccionDeError.AbrirAjustes, AccionDeErrorExtensiones.Para(texto));

    [Theory]
    [InlineData(TextosDeError.SinConexion)]
    [InlineData(TextosDeError.TiempoAgotado)]
    [InlineData(TextosDeError.ErrorGenerico)]
    public void Los_problemas_de_conexion_ofrecen_reintentar(string texto) =>
        Assert.Equal(AccionDeError.Reintentar, AccionDeErrorExtensiones.Para(texto));

    [Theory]
    [InlineData("Ese PIN no coincide. Probá de nuevo.")]
    [InlineData("Para fichar necesitamos una foto. Sacate una y listo.")]
    [InlineData(TextosDeError.SesionVencida)]
    [InlineData(TextosDeError.FichajeSinConfirmar)]   // reintentar a ciegas podría duplicar el fichaje
    [InlineData("")]
    [InlineData(null)]
    public void Lo_que_no_se_arregla_desde_el_cartel_no_ofrece_botones(string? texto) =>
        Assert.Equal(AccionDeError.Ninguna, AccionDeErrorExtensiones.Para(texto));

    [Fact]
    public void Las_etiquetas_son_verbos_cortos()
    {
        Assert.Equal("Abrir ajustes", AccionDeError.AbrirAjustes.Etiqueta());
        Assert.Equal("Reintentar", AccionDeError.Reintentar.Etiqueta());
        Assert.Null(AccionDeError.Ninguna.Etiqueta());
    }
}
