using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

namespace FichajeApp.Api.Controllers;

public class KioscoFichajeRequest
{
    public int EmpleadoId { get; set; }
    public string? Pin { get; set; }
    public string? FotoBase64 { get; set; }
}

// Solo para la cuenta del teléfono compartido de una sucursal (permiso "Fichar en kiosco")
[ApiController]
[Route("api/kiosco")]
[RequierePermiso(CatalogoPermisos.FicharEnKiosco)]
public class KioscoController : ControllerBase
{
    private readonly KioscoService _kiosco;
    private readonly IFichajeService _fichaje;

    public KioscoController(KioscoService kiosco, IFichajeService fichaje)
    {
        _kiosco = kiosco;
        _fichaje = fichaje;
    }

    // Si se pide foto (la app la saca antes de mandar el fichaje)
    [HttpGet("configuracion")]
    public async Task<IActionResult> Configuracion()
    {
        var c = await _fichaje.ConfiguracionAsync();
        return Ok(new { foto = c.Foto, retencionDias = c.RetencionFotosDias });
    }

    [HttpGet("empleados")]
    public async Task<IActionResult> Empleados() => Ok(await _kiosco.ListarAsync());

    // No devuelve 401 ante un PIN incorrecto: la app lo tomaría como sesión vencida
    [HttpPost("fichaje")]
    public async Task<IActionResult> Fichar([FromBody] KioscoFichajeRequest request)
    {
        var sucursal = User.FindFirst(ClaimTypes.Name)?.Value ?? "Kiosco";

        byte[]? foto = null;
        if (!string.IsNullOrWhiteSpace(request.FotoBase64) && (await _fichaje.ConfiguracionAsync()).Foto != ConfiguracionFichaje.FotoNo)
        {
            try { foto = ReglasFotos.Decodificar(request.FotoBase64); }
            catch (FormatException ex) { return BadRequest(new { mensaje = ex.Message, codigo = "FOTO_INVALIDA" }); }
        }

        var r = await _kiosco.FicharAsync(sucursal, request.EmpleadoId, request.Pin, foto);
        return r.Exito
            ? Ok(new { mensaje = r.Mensaje, tipo = r.Fichaje!.TipoFichaje })
            : BadRequest(new { mensaje = r.Mensaje, codigo = r.Codigo });
    }
}
