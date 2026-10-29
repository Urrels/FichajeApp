using System.Globalization;

namespace FichajeApp.Mobile.Services;

// Diálogos del sistema sobre la página actual. Los ViewModels de administración pasan por acá en vez de
// llamar a Shell.Current directamente, así el flujo de cada pantalla se lee de corrido.
public static class Dialogos
{
    private static Page Pagina => Shell.Current.CurrentPage;

    public static Task Aviso(string titulo, string mensaje) => Pagina.DisplayAlert(titulo, mensaje, "Aceptar");

    public static Task<bool> Confirmar(string titulo, string mensaje, string si = "Sí") =>
        Pagina.DisplayAlert(titulo, mensaje, si, "Cancelar");

    // Devuelve null si el usuario cancela
    public static async Task<string?> Texto(string titulo, string mensaje, string? inicial = null, int maximo = -1, Keyboard? teclado = null)
    {
        var texto = await Pagina.DisplayPromptAsync(titulo, mensaje, "Aceptar", "Cancelar",
            initialValue: inicial ?? "", maxLength: maximo, keyboard: teclado ?? Keyboard.Default);
        return texto?.Trim();
    }

    // Lista de opciones; devuelve la elegida o null si cancela
    public static async Task<string?> Elegir(string titulo, IEnumerable<string> opciones, string? destructiva = null)
    {
        var elegida = await Pagina.DisplayActionSheet(titulo, "Cancelar", destructiva, opciones.ToArray());
        return string.IsNullOrEmpty(elegida) || elegida == "Cancelar" ? null : elegida;
    }

    // Selección múltiple: cada toque marca o desmarca, "Listo" confirma. Devuelve null si cancela.
    public static async Task<List<int>?> ElegirVarios(string titulo, IReadOnlyList<(int Id, string Nombre)> opciones, IEnumerable<int> seleccion)
    {
        var elegidos = new HashSet<int>(seleccion);
        while (true)
        {
            var lineas = opciones.Select(o => (elegidos.Contains(o.Id) ? "✔ " : "☐ ") + o.Nombre).ToList();
            var respuesta = await Pagina.DisplayActionSheet($"{titulo} ({elegidos.Count})", "Cancelar", "Listo", lineas.ToArray());

            if (respuesta == "Listo") return elegidos.ToList();
            if (string.IsNullOrEmpty(respuesta) || respuesta == "Cancelar") return null;

            var indice = lineas.IndexOf(respuesta);
            if (indice < 0) continue;
            var id = opciones[indice].Id;
            if (!elegidos.Add(id)) elegidos.Remove(id);
        }
    }

    // "aaaa-mm-dd"; null si cancela o no es una fecha válida
    public static async Task<DateTime?> Fecha(string titulo, DateTime inicial)
    {
        var texto = await Texto(titulo, "Fecha (aaaa-mm-dd)", inicial.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 10);
        if (texto is null) return null;
        if (DateTime.TryParseExact(texto, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) return fecha.Date;
        await Aviso("Fecha inválida", "Usá el formato aaaa-mm-dd, por ejemplo 2026-10-05.");
        return null;
    }

    // "hh:mm"; null si cancela o no es una hora válida
    public static async Task<TimeSpan?> Hora(string titulo, string? inicial = null)
    {
        var texto = await Texto(titulo, "Hora (hh:mm, formato 24 h)", inicial, 5);
        if (texto is null) return null;
        if (TimeSpan.TryParseExact(texto, new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var hora) && hora < TimeSpan.FromHours(24))
            return hora;
        await Aviso("Hora inválida", "Usá el formato hh:mm, por ejemplo 08:30.");
        return null;
    }

    // Número con coma o punto decimal
    public static async Task<decimal?> Numero(string titulo, string mensaje, string? inicial = null)
    {
        var texto = await Texto(titulo, mensaje, inicial, 12, Keyboard.Numeric);
        if (texto is null) return null;
        if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)) return valor;
        await Aviso("Número inválido", "Ingresá un número, por ejemplo 1500 o 1500,50.");
        return null;
    }
}
