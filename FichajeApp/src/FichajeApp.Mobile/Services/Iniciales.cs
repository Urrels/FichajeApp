namespace FichajeApp.Mobile.Services;

// Las iniciales de los avatares: la primera letra de las dos primeras palabras.
// Solo cuentan las palabras que empiezan con letra o número, así un adorno como el candado de "Administrador 🔒"
// no termina dibujado como un cuadrito de "carácter faltante".
public static class Iniciales
{
    public static string De(string? texto) =>
        string.Concat((texto ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetterOrDigit(p[0]))
            .Take(2)
            .Select(p => char.ToUpperInvariant(p[0])));
}
