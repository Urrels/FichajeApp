using System.Security.Claims;
using FichajeApp.Api.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FichajeApp.Api.Controllers;

// Centro de notificaciones: cada persona ve solo las suyas
[ApiController]
[ManejarReglasNegocio]
[Authorize]
[Route("api/notificaciones")]
public class NotificacionesController : ControllerBase
{
    private readonly NotificacionesService _notificaciones;

    public NotificacionesController(NotificacionesService notificaciones) => _notificaciones = notificaciones;

    private int MiId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await _notificaciones.ListarAsync(MiId);
        return Ok(new
        {
            noLeidas = lista.Count(n => !n.Leida),
            items = lista.Select(n => new { n.Id, n.Titulo, n.Mensaje, n.Fecha, n.Leida })
        });
    }

    // Solo el número, para el globito del menú (consulta liviana)
    [HttpGet("cantidad")]
    public async Task<IActionResult> Cantidad() => Ok(new { noLeidas = await _notificaciones.ContarNoLeidasAsync(MiId) });

    [HttpPost("leer-todas")]
    public async Task<IActionResult> LeerTodas()
    {
        await _notificaciones.MarcarLeidasAsync(MiId);
        return Ok(new { mensaje = "Listo" });
    }

    [HttpPost("{id:int}/leer")]
    public async Task<IActionResult> Leer(int id)
    {
        await _notificaciones.MarcarLeidasAsync(MiId, id);
        return Ok(new { mensaje = "Listo" });
    }
}
