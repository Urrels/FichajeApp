using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Repositorios;

// Hoja ConfigFichaje: Clave | Valor (UBICACION, FOTO). Si no hay filas, rige el valor por defecto.
public class ConfiguracionFichajeSheetsRepositorio : IConfiguracionFichajeRepositorio
{
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly HojaSheets _hoja;

    public ConfiguracionFichajeSheetsRepositorio(GoogleSheetsClient cliente) =>
        _hoja = new HojaSheets(cliente, Planilla.ConfigFichaje);

    public async Task<ConfiguracionFichaje?> ObtenerAsync()
    {
        var filas = await _hoja.LeerAsync("A:B");
        string? Valor(string clave) =>
            filas.Where(f => f.Texto(0) == clave).Select(f => f.Texto(1)).FirstOrDefault(v => v.Length > 0);

        var ubicacion = Valor("UBICACION");
        var foto = Valor("FOTO");
        var modo = Valor("MODO");
        var precision = Valor("PRECISION");
        if (ubicacion is null && foto is null && modo is null && precision is null) return null;

        return new ConfiguracionFichaje
        {
            Ubicacion = ubicacion ?? ConfiguracionFichaje.UbicacionLibre,
            Foto = foto ?? ConfiguracionFichaje.FotoNo,
            Modo = modo ?? ConfiguracionFichaje.ModoPersonal, // las planillas anteriores no tienen esta fila: modo personal
            // 0 = sin dato: el servicio usa lo de appsettings
            PrecisionMaxMetros = int.TryParse(precision, out var p) ? p : 0
        };
    }

    public async Task GuardarAsync(ConfiguracionFichaje configuracion)
    {
        await _escritura.WaitAsync();
        try
        {
            await _hoja.ReemplazarTodoAsync(new List<IList<object>>
            {
                new List<object> { "UBICACION", configuracion.Ubicacion },
                new List<object> { "FOTO", configuracion.Foto },
                new List<object> { "MODO", configuracion.Modo },
                new List<object> { "PRECISION", configuracion.PrecisionMaxMetros }
            });
        }
        finally { _escritura.Release(); }
    }
}
