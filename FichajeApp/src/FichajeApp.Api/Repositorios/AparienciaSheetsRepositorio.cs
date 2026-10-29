using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Repositorios;

// Hoja Apariencia: Clave | Valor. Una celda de Google admite como máximo 50.000 caracteres, así que el logo
// (base64) se parte en filas "LOGO" de 40.000 que se leen en orden.
public class AparienciaSheetsRepositorio : IAparienciaRepositorio
{
    private const int TrozoDeLogo = 40_000;
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly HojaSheets _hoja;

    public AparienciaSheetsRepositorio(GoogleSheetsClient cliente) => _hoja = new HojaSheets(cliente, Planilla.Apariencia);

    public async Task<AparienciaGuardada> ObtenerAsync()
    {
        var filas = await _hoja.LeerAsync("A:B");
        string? Valor(string clave) =>
            filas.Where(f => f.Texto(0) == clave).Select(f => f.Texto(1)).FirstOrDefault(v => v.Length > 0);

        var resultado = new AparienciaGuardada
        {
            Nombre = Valor("NOMBRE"),
            ColorPrimario = Valor("COLOR_PRIMARIO"),
            ColorPrimarioTexto = Valor("COLOR_PRIMARIO_TEXTO"),
            ColorAcento = Valor("COLOR_ACENTO")
        };

        var base64 = string.Concat(filas.Where(f => f.Texto(0) == "LOGO").Select(f => f.Texto(1)));
        if (base64.Length > 0)
        {
            try { resultado.Logo = Convert.FromBase64String(base64); }
            catch (FormatException) { resultado.Logo = null; } // celda editada a mano: se ignora el logo
        }
        return resultado;
    }

    public async Task GuardarAsync(AparienciaGuardada a)
    {
        var filas = new List<IList<object>>();
        void Agregar(string clave, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor)) filas.Add(new List<object> { clave, valor });
        }

        Agregar("NOMBRE", a.Nombre);
        Agregar("COLOR_PRIMARIO", a.ColorPrimario);
        Agregar("COLOR_PRIMARIO_TEXTO", a.ColorPrimarioTexto);
        Agregar("COLOR_ACENTO", a.ColorAcento);

        if (a.Logo is { Length: > 0 })
        {
            var base64 = Convert.ToBase64String(a.Logo);
            for (int i = 0; i < base64.Length; i += TrozoDeLogo)
                filas.Add(new List<object> { "LOGO", base64.Substring(i, Math.Min(TrozoDeLogo, base64.Length - i)) });
        }

        await _escritura.WaitAsync();
        try { await _hoja.ReemplazarTodoAsync(filas); }
        finally { _escritura.Release(); }
    }
}
