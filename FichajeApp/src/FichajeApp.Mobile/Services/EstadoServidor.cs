namespace FichajeApp.Mobile.Services;

// ¿Responde el servidor? Se usa al cambiar la dirección del servidor desde el login (5 toques al logo): así el error se ve
// ahí y no recién al intentar entrar.
public class EstadoServidor
{
    private readonly HttpClient _http = new();
    private readonly IClienteService _clientes;

    public EstadoServidor(IClienteService clientes) => _clientes = clientes;

    public async Task<bool> RespondeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var r = await _http.GetAsync($"{_clientes.BaseUrl}/configuracion/estado", cts.Token);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
