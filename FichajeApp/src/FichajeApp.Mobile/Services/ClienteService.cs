namespace FichajeApp.Mobile.Services;

// Resuelve a qué API habla la app según el código de empresa que ingresa el usuario.
// Cada cliente tiene su propia instancia de la API (y su propia planilla).
// Para sumar un cliente: agregar una línea al diccionario y publicar una versión de la app.
public interface IClienteService
{
    string? Codigo { get; }
    string? BaseUrl { get; }
    bool TrySeleccionar(string codigo);
    void Persistir();
}

public class ClienteService : IClienteService
{
    private const string CodigoKey = "cliente_codigo";

    // Código (sin distinguir mayúsculas) -> URL base de la API de ese cliente.
    // TODO: reemplazar por las URLs reales de cada cliente.
    private static readonly Dictionary<string, string> Clientes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLIENTEA"] = "https://api-clientea.example.com/api",
        ["CLIENTEB"] = "https://api-clienteb.example.com/api",
#if DEBUG
        ["DEV"] = "https://localhost:7001/api", // emulador Android: usar http://10.0.2.2:<puerto>/api
#endif
    };

    public string? Codigo { get; private set; }
    public string? BaseUrl { get; private set; }

    public ClienteService()
    {
        // Recupera el cliente de la última sesión (no es un dato sensible)
        var guardado = Preferences.Default.Get<string?>(CodigoKey, null);
        if (!string.IsNullOrEmpty(guardado))
            TrySeleccionar(guardado);
    }

    public bool TrySeleccionar(string codigo)
    {
        codigo = codigo?.Trim() ?? string.Empty;
        if (!Clientes.TryGetValue(codigo, out var url))
            return false;

        Codigo = codigo.ToUpperInvariant();
        BaseUrl = url;
        return true;
    }

    // Se llama solo tras un login exitoso, para no recordar un código que no funcionó
    public void Persistir()
    {
        if (Codigo is not null)
            Preferences.Default.Set(CodigoKey, Codigo);
    }
}
