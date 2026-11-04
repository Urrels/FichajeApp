using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Controllers;

// Fotos de perfil. Verlas: cualquier cuenta con sesión (se muestran en las listas, el kiosco y el encabezado).
// Cambiar la propia: cualquier persona con sesión. Cambiar la de otro: ver UsuariosController.
[ApiController]
[ManejarReglasNegocio]
[Authorize]
[Route("api/perfil")]
public class PerfilController : ControllerBase
{
    private readonly IAlmacenPerfiles _perfiles;

    public PerfilController(IAlmacenPerfiles perfiles) => _perfiles = perfiles;

    public record FotoRequest(string FotoBase64);

    private int MiId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // { "2": 1730750000000, "5": ... }: quiénes tienen foto y de qué versión. El teléfono baja solo las que cambiaron.
    [HttpGet("versiones")]
    public async Task<IActionResult> Versiones() => Ok(await _perfiles.VersionesAsync());

    [HttpGet("{id:int}/foto")]
    public async Task<IActionResult> Foto(int id)
    {
        var foto = await _perfiles.LeerAsync(id);
        if (foto is null) return NotFound(new { mensaje = "Esa persona no tiene foto de perfil." });

        Response.Headers.CacheControl = "private,max-age=300";
        return File(foto.Value.Bytes, foto.Value.Mime);
    }

    [HttpPut("foto")]
    public async Task<IActionResult> GuardarMia([FromBody] FotoRequest r)
    {
        await _perfiles.GuardarAsync(MiId, Decodificar(r.FotoBase64));
        return Ok(new { mensaje = "Foto de perfil guardada" });
    }

    [HttpDelete("foto")]
    public async Task<IActionResult> QuitarMia()
    {
        await _perfiles.EliminarAsync(MiId);
        return Ok(new { mensaje = "Foto de perfil quitada" });
    }

    // Las reglas de la foto (formato y tamaño) fallan con un mensaje para la persona, no con un error del servidor
    internal static byte[] Decodificar(string? base64)
    {
        try { return ReglasFotos.Decodificar(base64 ?? string.Empty); }
        catch (FormatException ex) { throw new InvalidOperationException(ex.Message); }
    }
}
