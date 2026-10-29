using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// El administrador elige cómo ficha su empresa: si se pide ubicación y si se pide foto.
// Se guarda en la planilla y vale para todos los teléfonos al próximo fichaje.
public class ConfigFichajeViewModel : VmBase
{
    private static readonly Dictionary<string, string> Ubicaciones = new()
    {
        ["Ninguna (no se pide ni se guarda)"] = "Ninguna",
        ["Libre (se registra dónde ficha, con motivo si es un lugar nuevo)"] = "Libre",
        ["Solo en la sede (se rechaza fuera de ella)"] = "SedeFija"
    };

    private static readonly Dictionary<string, string> Fotos = new()
    {
        ["No sacar foto"] = "No",
        ["Opcional (se ofrece, se puede omitir)"] = "Opcional",
        ["Obligatoria (sin foto no se ficha)"] = "Obligatoria"
    };

    private readonly AdminApiService _api;
    private string _ubicacion = "Libre";
    private string _foto = "No";

    public ConfigFichajeViewModel(AdminApiService api) => _api = api;

    public string Ubicacion { get => _ubicacion; private set { if (Set(ref _ubicacion, value)) Notificar(nameof(UbicacionTexto)); } }
    public string Foto { get => _foto; private set { if (Set(ref _foto, value)) Notificar(nameof(FotoTexto)); } }

    private int _retencionDias = 90;
    public string RetencionTexto => _retencionDias > 0
        ? $"Las fotos se borran solas a los {_retencionDias} días (lo define el servidor)."
        : "Las fotos no se borran nunca (lo define el servidor).";

    public string UbicacionTexto => Ubicaciones.FirstOrDefault(u => u.Value == Ubicacion).Key ?? Ubicacion;
    public string FotoTexto => Fotos.FirstOrDefault(f => f.Value == Foto).Key ?? Foto;

    public Task CargarAsync() => ConEspera(async () =>
    {
        var r = await _api.ConfiguracionFichajeActual();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        Ubicacion = r.Datos.Ubicacion;
        Foto = r.Datos.Foto;
        _retencionDias = r.Datos.RetencionDias;
        Notificar(nameof(RetencionTexto));
        Estado = string.Empty;
    });

    public async Task ElegirUbicacionAsync()
    {
        var elegida = await Dialogos.Elegir("¿Se pide la ubicación?", Ubicaciones.Keys);
        if (elegida is not null) Ubicacion = Ubicaciones[elegida];
    }

    public async Task ElegirFotoAsync()
    {
        var elegida = await Dialogos.Elegir("¿Se pide una foto al fichar?", Fotos.Keys);
        if (elegida is not null) Foto = Fotos[elegida];
    }

    public Task GuardarAsync() => ConEspera(async () =>
    {
        var r = await _api.GuardarConfiguracionFichaje(Ubicacion, Foto);
        Estado = r.Exito ? "Guardado. Se aplica en el próximo fichaje." : r.Mensaje;
    });
}
