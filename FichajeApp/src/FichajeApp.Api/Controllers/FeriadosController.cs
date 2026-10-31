using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Controllers;

// Feriados del año para cualquier persona con sesión (la app los usa para avisar y mostrar). Cache de 24 h en el servidor.
[ApiController]
[Route("api/feriados")]
[Authorize]
[ManejarReglasNegocio]
public class FeriadosController : ControllerBase
{
    private readonly FeriadosService _feriados;
    private readonly TimeProvider _tiempo;

    public FeriadosController(FeriadosService feriados, TimeProvider tiempo)
    {
        _feriados = feriados;
        _tiempo = tiempo;
    }

    [HttpGet]
    public async Task<IActionResult> Anio([FromQuery] int? anio)
    {
        var lista = await _feriados.ObtenerAnioAsync(anio ?? _tiempo.GetLocalNow().Year);
        Response.Headers.CacheControl = "private, max-age=3600";
        return Ok(lista);
    }
}
