using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Dominio.Seguridad;
using FichajeApp.Api.Models;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Controllers;

// Lo que se ve antes de iniciar sesión (colores, nombre y logo): es la marca de la empresa, no hay nada sensible
[ApiController]
[AllowAnonymous]
[Route("api/apariencia")]
public class AparienciaController : ControllerBase
{
    private readonly AparienciaService _apariencia;
    public AparienciaController(AparienciaService apariencia) => _apariencia = apariencia;

    [HttpGet]
    public async Task<IActionResult> Obtener() => Ok(await _apariencia.ObtenerPublicaAsync());

    // La app pide el logo con ?v=<versión>: al cambiar la imagen cambia la versión y se baja de nuevo
    [HttpGet("logo")]
    public async Task<IActionResult> Logo()
    {
        var logo = await _apariencia.ObtenerLogoAsync();
        if (logo is null) return NotFound();

        Response.Headers.CacheControl = "public,max-age=86400";
        return File(logo.Value.Bytes, logo.Value.Mime);
    }
}

[Route("api/admin/apariencia")]
[RequierePermiso(CatalogoPermisos.GestionarApariencia)]
public class AparienciaAdminController : AdminControllerBase
{
    private readonly AparienciaService _apariencia;
    public AparienciaAdminController(AparienciaService apariencia) => _apariencia = apariencia;

    [HttpPut]
    public async Task<IActionResult> Guardar([FromBody] GuardarAparienciaRequest request)
    {
        await _apariencia.GuardarAsync(Actor, request);
        return Ok(new { mensaje = "Apariencia guardada" });
    }
}

// Cómo ficha este cliente: ubicación (Ninguna / Libre / SedeFija) y foto (No / Opcional / Obligatoria)
[Route("api/admin/configuracion-fichaje")]
[RequierePermiso(CatalogoPermisos.ConfigurarFichaje)]
public class ConfiguracionFichajeAdminController : AdminControllerBase
{
    private readonly ConfiguracionFichajeService _configuracion;
    public ConfiguracionFichajeAdminController(ConfiguracionFichajeService configuracion) => _configuracion = configuracion;

    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        var c = await _configuracion.ObtenerAsync();
        return Ok(new { ubicacion = c.Ubicacion, foto = c.Foto, modo = c.Modo, retencionDias = c.RetencionFotosDias, precisionMaxMetros = c.PrecisionMaxMetros });
    }

    [HttpPut]
    public async Task<IActionResult> Guardar([FromBody] GuardarConfiguracionFichajeRequest request)
    {
        await _configuracion.GuardarAsync(Actor, request);
        return Ok(new { mensaje = "Configuración de fichaje guardada" });
    }
}
// Los lugares conocidos donde se puede fichar con "Ubicación libre" (nombre, punto en el mapa y radio en metros)
[Route("api/admin/sitios")]
[RequierePermiso(CatalogoPermisos.ConfigurarFichaje)]
public class SitiosAdminController : AdminControllerBase
{
    private readonly SitiosAdminService _sitios;
    public SitiosAdminController(SitiosAdminService sitios) => _sitios = sitios;

    [HttpGet]
    public async Task<IActionResult> Listar() => Ok(await _sitios.ListarAsync());

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] GuardarSitioRequest request)
    {
        var id = await _sitios.CrearAsync(Actor, request);
        return Ok(new { id, mensaje = "Lugar guardado" });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] GuardarSitioRequest request)
    {
        await _sitios.ActualizarAsync(Actor, id, request);
        return Ok(new { mensaje = "Lugar guardado" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        await _sitios.EliminarAsync(Actor, id);
        return Ok(new { mensaje = "Lugar eliminado" });
    }
}
