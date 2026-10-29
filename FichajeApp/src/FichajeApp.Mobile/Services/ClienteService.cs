using System.Text.Json;

namespace FichajeApp.Mobile.Services;

public record ColoresCliente(string Primario, string PrimarioTexto, string Acento);

public record ClienteConfig(string Nombre, string ApiBaseUrl, ColoresCliente Colores);

// Identidad del cliente al que pertenece este build de la app (URL de la API, nombre y colores).
// La config viene de Clientes/<cliente>/cliente.json, embebida al compilar con -p:Cliente=<cliente>.
public interface IClienteService
{
    string Nombre { get; }
    string BaseUrl { get; }
    void AplicarTema();
}

public class ClienteService : IClienteService
{
    private readonly ClienteConfig _config;

    public ClienteService()
    {
        using var stream = typeof(ClienteService).Assembly.GetManifestResourceStream("cliente.json")
            ?? throw new InvalidOperationException(
                "Falta cliente.json embebido. Ver FichajeApp.Mobile.Clientes.csproj.snippet.txt.");

        _config = JsonSerializer.Deserialize<ClienteConfig>(stream,
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? throw new InvalidOperationException("cliente.json inválido.");
    }

    public string Nombre => _config.Nombre;
    public string BaseUrl => _config.ApiBaseUrl;

    // Pisa los colores base de Resources/Styles/Tema.xaml con los del cliente.
    // Las pantallas usan DynamicResource, así que se actualizan solas.
    public void AplicarTema()
    {
        var recursos = Application.Current!.Resources;
        recursos["NombreCliente"] = Nombre;
        recursos["ColorPrimario"] = Color.FromArgb(_config.Colores.Primario);
        recursos["ColorPrimarioTexto"] = Color.FromArgb(_config.Colores.PrimarioTexto);
        recursos["ColorAcento"] = Color.FromArgb(_config.Colores.Acento);
    }
}
