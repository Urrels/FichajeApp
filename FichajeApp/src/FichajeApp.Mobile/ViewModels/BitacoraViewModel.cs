using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

public class BitacoraViewModel : VmBase
{
    private const int Tamanio = 30;

    private readonly AdminApiService _api;
    private string _usuario = string.Empty;
    private string _accion = string.Empty;
    private int _pagina = 1;
    private int _total;
    private bool _hayMas;

    public BitacoraViewModel(AdminApiService api) => _api = api;

    public ObservableCollection<EntradaBitacora> Entradas { get; } = new();

    public string Usuario
    {
        get => _usuario;
        set => Set(ref _usuario, value);
    }

    public string Accion
    {
        get => _accion;
        set => Set(ref _accion, value);
    }

    public bool HayMas
    {
        get => _hayMas;
        private set => Set(ref _hayMas, value);
    }

    // Busca desde la primera página con los filtros actuales
    public Task BuscarAsync() => ConEspera(async () =>
    {
        _pagina = 1;
        Entradas.Clear();
        await CargarPaginaAsync();
    });

    public Task MasAsync() => ConEspera(async () =>
    {
        _pagina++;
        await CargarPaginaAsync();
    });

    private async Task CargarPaginaAsync()
    {
        Estado = "Cargando...";
        var r = await _api.Bitacora(Usuario, Accion, _pagina, Tamanio);
        if (!r.Exito || r.Datos is null)
        {
            Estado = r.Mensaje;
            HayMas = false;
            return;
        }

        _total = r.Datos.Total;
        foreach (var e in r.Datos.Items) Entradas.Add(e);
        HayMas = Entradas.Count < _total;
        Estado = _total == 0 ? "No hay registros con esos filtros" : $"{Entradas.Count} de {_total}";
    }
}
