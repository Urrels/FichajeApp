using System.Globalization;
using System.Text;
using FichajeApp.Api.Dominio.Liquidacion;

namespace FichajeApp.Api.Servicios;

// Informe de una liquidación cerrada en PDF. Está armado a mano (un PDF mínimo con las fuentes estándar Helvetica y Courier,
// que todo visor trae): así no hace falta ninguna librería de terceros ni licencia, y anda igual en Windows, Linux y Docker.
// Contenido: resumen por empleado con el total y, después, el detalle día por día de cada uno.
public static class ReportePdf
{
    private const double Ancho = 595, Alto = 842;         // A4 en puntos
    private const double MargenX = 40, Tope = 800, Piso = 56;
    private const double CharCourier = 0.6;               // cada carácter de Courier mide 0,6 del tamaño: sirve para alinear números

    private static readonly CultureInfo Ar = new("es-AR");
    private static readonly Encoding Latin1 = Encoding.Latin1;

    // Una página es una lista de instrucciones ya escritas del contenido (texto y líneas)
    private sealed class Pagina { public readonly List<string> Ops = new(); }

    private sealed class Dibujo
    {
        public readonly List<Pagina> Paginas = new() { new Pagina() };
        public double Y = Tope;
        public Pagina Actual => Paginas[^1];

        public void SaltoSiNoEntra(double alto)
        {
            if (Y - alto >= Piso) return;
            Paginas.Add(new Pagina());
            Y = Tope;
        }

        // fuente: F1 Helvetica, F2 Helvetica-Bold, F3 Courier
        public void Texto(double x, string fuente, double tam, string texto, double? y = null) =>
            Actual.Ops.Add(string.Create(CultureInfo.InvariantCulture, $"BT /{fuente} {tam} Tf {x:0.##} {(y ?? Y):0.##} Td ({Escapar(texto)}) Tj ET"));

        // Número alineado a la derecha en Courier: el borde derecho queda en xDerecha
        public void Numero(double xDerecha, double tam, string texto) =>
            Texto(xDerecha - texto.Length * tam * CharCourier, "F3", tam, texto);

        public void Linea(double x1, double x2, double? y = null) =>
            Actual.Ops.Add(string.Create(CultureInfo.InvariantCulture, $"0.6 w {x1:0.##} {(y ?? Y):0.##} m {x2:0.##} {(y ?? Y):0.##} l S"));
    }

    public static byte[] Liquidacion(PeriodoLiquidado periodo, IReadOnlyList<LiquidacionDia> dias, string empresa, DateTime generado)
    {
        var resumen = LiquidacionService.Resumir(dias);
        var d = new Dibujo();

        // --- Encabezado ---
        d.Texto(MargenX, "F2", 17, "Liquidación de horas"); d.Y -= 18;
        if (!string.IsNullOrWhiteSpace(empresa)) { d.Texto(MargenX, "F1", 11, empresa); d.Y -= 15; }
        d.Texto(MargenX, "F1", 10, $"Período: {periodo.Desde:dd/MM/yyyy} al {periodo.Hasta:dd/MM/yyyy}   ·   Estado: {periodo.Estado}"); d.Y -= 13;
        d.Texto(MargenX, "F1", 9, $"Cerrada por {periodo.CerradaPor} el {periodo.FechaCierre:dd/MM/yyyy HH:mm}"
            + (periodo.FechaPago is { } pago ? $"   ·   Pagada por {periodo.PagadaPor} el {pago:dd/MM/yyyy}" : string.Empty)); d.Y -= 22;

        // --- Resumen por empleado ---
        d.Texto(MargenX, "F2", 12, "Resumen por empleado"); d.Y -= 16;
        EncabezadoResumen(d);
        foreach (var r in resumen)
        {
            d.SaltoSiNoEntra(14);
            d.Texto(MargenX, "F1", 9, Recortar(r.Empleado, 30));
            d.Numero(235, 9, Horas(r.MinTrabajados));
            d.Numero(280, 9, Horas(r.MinPagables));
            d.Numero(328, 9, Horas(r.MinNormales));
            d.Numero(372, 9, Horas(r.MinExtra));
            d.Numero(418, 9, Horas(r.MinFeriado));
            d.Numero(450, 9, r.Ausencias.ToString(Ar));
            d.Numero(555, 9, Dinero(r.Importe));
            d.Y -= 13;
        }
        d.SaltoSiNoEntra(24);
        d.Linea(MargenX, 555, d.Y + 9);
        d.Y -= 4;
        d.Texto(MargenX, "F2", 10, "Total a pagar");
        d.Numero(555, 10, Dinero(resumen.Sum(r => r.Importe)));
        d.Y -= 28;

        // --- Detalle por día ---
        d.SaltoSiNoEntra(40);
        d.Texto(MargenX, "F2", 12, "Detalle por día"); d.Y -= 18;
        foreach (var grupo in dias.Where(x => !x.Ausente || x.Importe != 0 || x.MinTrabajados > 0)
                     .GroupBy(x => x.Empleado).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            d.SaltoSiNoEntra(44);
            d.Texto(MargenX, "F2", 10, Recortar(grupo.Key, 40)); d.Y -= 13;
            d.Texto(MargenX + 8, "F2", 8, "Fecha"); d.Texto(120, "F2", 8, "Horario"); d.Texto(290, "F2", 8, "Asignadas (h)"); d.Texto(380, "F2", 8, "A pagar (h)");
            d.Texto(440, "F2", 8, "Importe"); d.Y -= 4;
            d.Linea(MargenX, 555); d.Y -= 10;

            foreach (var x in grupo.OrderBy(x => x.Fecha))
            {
                d.SaltoSiNoEntra(14);
                d.Texto(MargenX + 8, "F3", 8, $"{x.Fecha:dd/MM} {NombreDia(x.Fecha)}");
                d.Texto(120, "F3", 8, Horario(x));
                if (x.MinAsignados > 0) d.Numero(338, 8, Horas(x.MinAsignados));
                d.Numero(430, 8, Horas(x.MinPagables));
                d.Numero(555, 8, Dinero(x.Importe));
                if (!string.IsNullOrWhiteSpace(x.Observacion))
                {
                    d.Y -= 10;
                    d.SaltoSiNoEntra(12);
                    d.Texto(MargenX + 16, "F1", 7, Recortar(x.Observacion!, 110));
                }
                d.Y -= 11;
            }
            d.Y -= 8;
        }

        return Armar(d.Paginas, generado);
    }

    private static void EncabezadoResumen(Dibujo d)
    {
        d.Texto(MargenX, "F2", 8, "Empleado");
        d.Texto(196, "F2", 8, "Trabajadas"); d.Texto(251, "F2", 8, "A pagar"); d.Texto(293, "F2", 8, "Normales");
        d.Texto(351, "F2", 8, "Extra"); d.Texto(388, "F2", 8, "Dom/Fer"); d.Texto(433, "F2", 8, "Aus.");
        d.Texto(522, "F2", 8, "Importe");
        d.Y -= 4;
        d.Linea(MargenX, 555);
        d.Y -= 11;
    }

    // ---- Armado del archivo PDF ----

    private static byte[] Armar(List<Pagina> paginas, DateTime generado)
    {
        // Objetos: 1 catálogo, 2 páginas, 3-5 fuentes, y desde el 6 de a pares: página y su contenido
        var cuerpos = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // 2: se completa abajo, cuando se sabe cuántas páginas hay
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"
        };

        var referencias = new List<string>();
        for (int i = 0; i < paginas.Count; i++)
        {
            int idPagina = cuerpos.Count + 1, idContenido = idPagina + 1;
            referencias.Add($"{idPagina} 0 R");

            var pie = string.Create(CultureInfo.InvariantCulture,
                $"BT /F1 8 Tf {MargenX} 28 Td ({Escapar($"Generado el {generado:dd/MM/yyyy HH:mm}")}) Tj ET BT /F1 8 Tf 480 28 Td ({Escapar($"Página {i + 1} de {paginas.Count}")}) Tj ET");
            var contenido = string.Join("\n", paginas[i].Ops) + "\n" + pie;

            cuerpos.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Ancho} {Alto}] /Contents {idContenido} 0 R " +
                        "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> >>");
            cuerpos.Add($"<< /Length {Latin1.GetByteCount(contenido)} >>\nstream\n{contenido}\nendstream");
        }
        cuerpos[1] = $"<< /Type /Pages /Kids [{string.Join(' ', referencias)}] /Count {paginas.Count} >>";

        using var salida = new MemoryStream();
        void Escribir(string texto) { var b = Latin1.GetBytes(texto); salida.Write(b, 0, b.Length); }

        Escribir("%PDF-1.4\n%âãÏÓ\n");
        var posiciones = new List<long>();
        for (int i = 0; i < cuerpos.Count; i++)
        {
            posiciones.Add(salida.Position);
            Escribir($"{i + 1} 0 obj\n{cuerpos[i]}\nendobj\n");
        }

        var inicioXref = salida.Position;
        Escribir($"xref\n0 {cuerpos.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in posiciones) Escribir($"{p:0000000000} 00000 n \n");
        Escribir($"trailer\n<< /Size {cuerpos.Count + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return salida.ToArray();
    }

    // ---- Texto ----

    // Lo que no entra en Latin-1 se reemplaza por "?"; paréntesis y barra se escapan; sin saltos de línea
    private static string Escapar(string texto)
    {
        var sb = new StringBuilder(texto.Length + 8);
        foreach (var c in texto)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (char.IsControl(c)) sb.Append(' ');
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..(max - 1)] + ".";

    private static string Horas(int minutos) => LiquidacionCalculo.Horas(minutos).ToString("0.##", Ar);

    private static string Dinero(decimal valor) => "$ " + valor.ToString("N2", Ar);

    private static string Horario(LiquidacionDia x) =>
        x.Ausente ? "Ausente" : x.Ingreso is { } i && x.Egreso is { } e ? $"{i:HH:mm} - {e:HH:mm}" : "-";

    private static string NombreDia(DateTime fecha) => fecha.ToString("ddd", Ar).TrimEnd('.');
}
