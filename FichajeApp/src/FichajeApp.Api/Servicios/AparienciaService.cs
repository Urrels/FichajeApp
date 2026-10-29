using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Dominio.Apariencia;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Servicios;

// Apariencia de la app (colores, nombre y logo) que el administrador cambia desde la app, sin recompilar.
// No incluye el ícono del launcher: ese lo fija Android al instalar y solo cambia con una versión nueva.
public class AparienciaService
{
    private const string ClaveCache = "apariencia";
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);
    private const string BlancoPorDefecto = "#FFFFFF"; // color del texto cuando no se define uno

    private readonly IAparienciaRepositorio _repositorio;
    private readonly BitacoraService _bitacora;
    private readonly IMemoryCache _cache;

    public AparienciaService(IAparienciaRepositorio repositorio, BitacoraService bitacora, IMemoryCache cache)
    {
        _repositorio = repositorio;
        _bitacora = bitacora;
        _cache = cache;
    }

    private async Task<AparienciaGuardada> LeerAsync() =>
        (await _cache.GetOrCreateAsync(ClaveCache, entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Vida;
            return _repositorio.ObtenerAsync();
        }))!;

    public async Task<AparienciaPublicaDto> ObtenerPublicaAsync()
    {
        var a = await LeerAsync();
        return new AparienciaPublicaDto
        {
            Nombre = a.Nombre,
            ColorPrimario = a.ColorPrimario,
            ColorPrimarioTexto = a.ColorPrimarioTexto,
            ColorAcento = a.ColorAcento,
            TieneLogo = a.Logo is { Length: > 0 },
            VersionLogo = a.Logo is { Length: > 0 } ? ReglasApariencia.VersionDe(a.Logo) : null
        };
    }

    public async Task<(byte[] Bytes, string Mime)?> ObtenerLogoAsync()
    {
        var logo = (await LeerAsync()).Logo;
        if (logo is not { Length: > 0 }) return null;

        var formato = ReglasApariencia.DetectarFormato(logo);
        return formato == ReglasApariencia.FormatoLogo.Desconocido ? null : (logo, ReglasApariencia.TipoMime(formato));
    }

    // Reemplaza la apariencia: lo que llega vacío vuelve al valor del cliente. El logo solo cambia si se manda uno
    // nuevo (LogoBase64) o se pide quitarlo (QuitarLogo).
    public async Task GuardarAsync(string actor, GuardarAparienciaRequest r)
    {
        var nombre = (r.Nombre ?? "").Trim();
        if (nombre.Length > ReglasApariencia.LargoMaximoNombre)
            throw new InvalidOperationException($"El nombre no puede superar los {ReglasApariencia.LargoMaximoNombre} caracteres.");

        var primario = ColorOpcional(r.ColorPrimario, "principal");
        var texto = ColorOpcional(r.ColorPrimarioTexto, "del texto");
        var acento = ColorOpcional(r.ColorAcento, "de acento");

        // Los botones llevan el texto sobre el color principal: si no se lee, se rechaza
        if (primario != null && ReglasApariencia.Contraste(primario, texto ?? BlancoPorDefecto) < ReglasApariencia.ContrasteMinimo)
            throw new InvalidOperationException(
                "El texto de los botones no se lee bien sobre ese color principal. Elegí un color más oscuro, o un texto de otro color.");

        var actual = await _repositorio.ObtenerAsync();
        var logo = actual.Logo;
        if (r.QuitarLogo) logo = null;
        if (!string.IsNullOrWhiteSpace(r.LogoBase64)) logo = DecodificarLogo(r.LogoBase64);

        await _repositorio.GuardarAsync(new AparienciaGuardada
        {
            Nombre = nombre.Length == 0 ? null : nombre,
            ColorPrimario = primario,
            ColorPrimarioTexto = texto,
            ColorAcento = acento,
            Logo = logo
        });
        _cache.Remove(ClaveCache);

        await _bitacora.RegistrarAsync(actor, "APARIENCIA_MODIFICADA");
    }

    private static string? ColorOpcional(string? color, string cual)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        return ReglasApariencia.NormalizarColor(color)
               ?? throw new InvalidOperationException($"El color {cual} no es válido. Usá el formato #RRGGBB, por ejemplo #1B5E9B.");
    }

    private static byte[] DecodificarLogo(string base64)
    {
        // Acepta "data:image/png;base64,...."
        var coma = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma >= 0) base64 = base64[(coma + 1)..];

        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("La imagen del logo no es válida."); }

        if (bytes.Length > ReglasApariencia.TamanioMaximoLogoBytes)
            throw new InvalidOperationException(
                $"El logo es demasiado pesado (máximo {ReglasApariencia.TamanioMaximoLogoBytes / 1024} KB). Usá una imagen más chica.");
        if (ReglasApariencia.DetectarFormato(bytes) == ReglasApariencia.FormatoLogo.Desconocido)
            throw new InvalidOperationException("El logo tiene que ser una imagen PNG o JPG.");
        return bytes;
    }
}
