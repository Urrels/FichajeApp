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

    private static readonly Dictionary<string, string> Modos = new()
    {
        ["Personal (cada persona ficha en su propio teléfono)"] = "Personal",
        ["Kiosco (un teléfono por sucursal para todos los empleados)"] = "Kiosco"
    };

    // Hasta cuántos metros de error del GPS se acepta un fichaje. Adentro de un edificio el GPS suele dar de 30 a 150 m.
    private static readonly Dictionary<string, int> Precisiones = new()
    {
        ["Estricta · 30 m (solo al aire libre)"] = 30,
        ["Normal · 50 m"] = 50,
        ["Flexible · 100 m (sirve dentro de edificios)"] = 100,
        ["Muy flexible · 200 m (casi todo pasa)"] = 200
    };

    private readonly AdminApiService _api;
    private string _modo = "Personal";
    private int _precision = 50;
    private string _ubicacion = "Libre";
    private string _foto = "No";

    private readonly IFeedback _feedback;

    public ConfigFichajeViewModel(AdminApiService api, IFeedback feedback)
    {
        _api = api;
        _feedback = feedback;
    }

    public string Ubicacion { get => _ubicacion; private set { if (Set(ref _ubicacion, value)) Notificar(nameof(UbicacionTexto)); } }
    public string Modo { get => _modo; private set { if (Set(ref _modo, value)) Notificar(nameof(ModoTexto)); } }
    public int Precision { get => _precision; private set { if (Set(ref _precision, value)) Notificar(nameof(PrecisionTexto)); } }
    public string PrecisionTexto => Precisiones.FirstOrDefault(p => p.Value == Precision).Key ?? $"{Precision} m (definido en el servidor)";

    public string ModoTexto => Modos.FirstOrDefault(m => m.Value == Modo).Key ?? Modo;

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
        Modo = r.Datos.Modo;
        Precision = r.Datos.PrecisionMaxMetros;
        _retencionDias = r.Datos.RetencionDias;
        Notificar(nameof(RetencionTexto));
        Estado = string.Empty;
    });

    public async Task ElegirUbicacionAsync()
    {
        var elegida = await Dialogos.Elegir("¿Se pide la ubicación?", Ubicaciones.Keys);
        if (elegida is not null) Ubicacion = Ubicaciones[elegida];
    }

    public async Task ElegirPrecisionAsync()
    {
        var elegida = await Dialogos.Elegir("¿Qué tan precisa tiene que ser la ubicación?", Precisiones.Keys);
        if (elegida is not null) Precision = Precisiones[elegida];
    }

    public async Task ElegirModoAsync()
    {
        var elegida = await Dialogos.Elegir("¿Cómo ficha tu equipo?", Modos.Keys);
        if (elegida is not null) Modo = Modos[elegida];
    }

    public async Task ElegirFotoAsync()
    {
        var elegida = await Dialogos.Elegir("¿Se pide una foto al fichar?", Fotos.Keys);
        if (elegida is not null) Foto = Fotos[elegida];
    }

    public Task GuardarAsync() => ConEspera(async () =>
    {
        var r = await _api.GuardarConfiguracionFichaje(Ubicacion, Foto, Modo, Precision);
        if (!r.Exito) { await _feedback.ErrorAsync(TextosDeError.Para(null, r.Mensaje)); return; }

        // El paso siguiente del modo kiosco queda a la vista en pantalla; el resto es solo una confirmación corta
        Estado = Modo == "Kiosco"
            ? "Ahora, en Usuarios, creá una cuenta por sucursal (por ejemplo «Sucursal Centro») y asignale el rol Kiosco."
            : string.Empty;
        await _feedback.ExitoAsync("Cambios guardados. Se aplican desde el próximo fichaje.");
    });
}
