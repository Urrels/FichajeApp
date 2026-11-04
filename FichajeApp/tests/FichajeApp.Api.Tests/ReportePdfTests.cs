using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FichajeApp.Api.Dominio.Liquidacion;
using FichajeApp.Api.Servicios;

namespace FichajeApp.Api.Tests;

public class ReportePdfTests
{
    private static PeriodoLiquidado Periodo() => new()
    {
        Id = 7, Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 10, 31), Estado = "Cerrada",
        CerradaPor = "admin@test.com", FechaCierre = new DateTime(2026, 11, 2, 9, 30, 0)
    };

    private static LiquidacionDia Dia(string empleado, int dia, int minutos, decimal importe, string? obs = null) => new()
    {
        EmpleadoId = empleado.GetHashCode(), Empleado = empleado, Fecha = new DateTime(2026, 10, dia),
        Ingreso = new DateTime(2026, 10, dia, 8, 0, 0), Egreso = new DateTime(2026, 10, dia, 8, 0, 0).AddMinutes(minutos),
        MinTrabajados = minutos, MinPagables = minutos, MinNormales = minutos, ValorHora = 1000m, Importe = importe, Observacion = obs
    };

    private static string Texto(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void ElPdf_TieneEncabezadoYFinDeArchivoValidos()
    {
        var pdf = ReportePdf.Liquidacion(Periodo(), new[] { Dia("Ana Empleada", 5, 480, 8000m) }, "Lion Fitness", new DateTime(2026, 11, 3));
        var t = Texto(pdf);

        Assert.StartsWith("%PDF-1.4", t);
        Assert.EndsWith("%%EOF\n", t);
        Assert.Contains("Liquidación de horas", t);
        Assert.Contains("Lion Fitness", t);
        Assert.Contains("Ana Empleada", t);
    }

    [Fact]
    public void LaTablaDeReferencias_ApuntaExactoACadaObjeto()
    {
        var dias = Enumerable.Range(1, 28).Select(d => Dia("Ana Empleada", d, 480, 8000m)).ToList();
        var pdf = ReportePdf.Liquidacion(Periodo(), dias, "", new DateTime(2026, 11, 3));
        var t = Texto(pdf);

        // startxref apunta a la palabra "xref"
        var inicio = int.Parse(t[(t.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.Equal("xref", t.Substring(inicio, 4));

        // cada entrada de la tabla apunta al "N 0 obj" correspondiente
        var entradas = t[(inicio + 5)..].Split('\n').Skip(1).Where(l => l.EndsWith(" n ")).ToList();
        Assert.NotEmpty(entradas);
        for (int i = 0; i < entradas.Count; i++)
        {
            var offset = int.Parse(entradas[i][..10]);
            Assert.StartsWith($"{i + 1} 0 obj", t[offset..]);
        }
    }

    [Fact]
    public void ConMuchosDias_ReparteEnVariasPaginas_YNumeraElPie()
    {
        var dias = new List<LiquidacionDia>();
        foreach (var persona in new[] { "Ana", "Beto", "Carla", "Diego", "Eva" })
            dias.AddRange(Enumerable.Range(1, 28).Select(d => Dia(persona, d, 480, 8000m)));

        var t = Texto(ReportePdf.Liquidacion(Periodo(), dias, "", new DateTime(2026, 11, 3)));

        var paginas = int.Parse(System.Text.RegularExpressions.Regex.Match(t, @"/Count (\d+)").Groups[1].Value);
        Assert.True(paginas >= 3);
        Assert.Contains($"Página 1 de {paginas}", t);
        Assert.Contains($"Página {paginas} de {paginas}", t);
    }

    [Fact]
    public void LosCaracteresEspeciales_NoRompenElArchivo()
    {
        var dias = new[] { Dia("Ana (la \\ jefa) ñandú 日本", 5, 480, 8000m, "Observación con (paréntesis) y \\ barra") };

        var t = Texto(ReportePdf.Liquidacion(Periodo(), dias, "Gym (Centro)", new DateTime(2026, 11, 3)));

        Assert.Contains(@"Ana \(la \\ jefa\) ñandú ??", t);   // Latin-1: la ñ se conserva, lo que no entra pasa a "?"
        Assert.Contains(@"Observación con \(paréntesis\) y \\ barra", t);
    }

    [Fact]
    public void SinDias_IgualGeneraUnPdfConElTotalEnCero()
    {
        var t = Texto(ReportePdf.Liquidacion(Periodo(), Array.Empty<LiquidacionDia>(), "", new DateTime(2026, 11, 3)));

        Assert.StartsWith("%PDF-1.4", t);
        Assert.Contains("Total a pagar", t);
        Assert.Contains("0,00", t);
    }
}

public class DescargaDeLiquidacionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    public DescargaDeLiquidacionTests(ApiFactory fabrica)
    {
        _fabrica = fabrica;
        _fabrica.Datos.LiquidacionRepo.Periodos.Add(new PeriodoLiquidado
        {
            Id = 1, Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 10, 7), Estado = "Cerrada",
            CerradaPor = "admin@test.com", FechaCierre = new DateTime(2026, 10, 8)
        });
        _fabrica.Datos.LiquidacionRepo.Dias[1] = new List<LiquidacionDia>
        {
            new() { EmpleadoId = 2, Empleado = "Ana Empleada", Fecha = new DateTime(2026, 10, 5), MinTrabajados = 480, MinPagables = 480,
                    MinNormales = 480, ValorHora = 1000m, Importe = 8000m,
                    Ingreso = new DateTime(2026, 10, 5, 8, 0, 0), Egreso = new DateTime(2026, 10, 5, 16, 0, 0) }
        };
    }

    private async Task<HttpClient> ClienteDe(string email, string pin)
    {
        var cliente = _fabrica.CreateClient();
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new { email, pin });
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task Pdf_ElAdministradorLoDescarga()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.GetAsync("/api/admin/liquidacion/liquidaciones/1/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("20261001-20261007", r.Content.Headers.ContentDisposition?.FileName ?? "");
        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(await r.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Pdf_UnEmpleadoComun_NoPuede()
    {
        var ana = await ClienteDe("ana@test.com", Entorno.PinAna);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/admin/liquidacion/liquidaciones/1/pdf")).StatusCode);
    }

    [Fact]
    public async Task Pdf_UnaLiquidacionQueNoExiste_DaUnMensajeClaro()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.GetAsync("/api/admin/liquidacion/liquidaciones/999/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("no existe", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Csv_SigueFuncionando()
    {
        var admin = await ClienteDe("admin@test.com", Entorno.PinAdmin);

        var r = await admin.GetAsync("/api/admin/liquidacion/liquidaciones/1/csv");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Ana Empleada", await r.Content.ReadAsStringAsync());
    }
}
