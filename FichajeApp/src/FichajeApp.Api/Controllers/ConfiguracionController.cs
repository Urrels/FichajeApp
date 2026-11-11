using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FichajeApp.Api.Controllers;

[ApiController]
[Route("api/configuracion")]
[AllowAnonymous]
public class ConfiguracionController : ControllerBase
{
    // Las versiones de la app que ya están instaladas lo consultan al abrir. El servidor siempre está configurado (los datos
    // viven en la base SQL), así que responde siempre lo mismo: no se puede quitar sin que esas versiones lo tomen como "sin configurar".
    [HttpGet("estado")]
    public IActionResult Estado() => Ok(new { configurada = true, cuentaDeServicio = (string?)null, aviso = (string?)null, almacenamiento = "sql" });
}
