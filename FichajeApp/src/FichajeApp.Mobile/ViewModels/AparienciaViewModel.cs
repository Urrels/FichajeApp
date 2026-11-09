using Microsoft.Maui.Graphics;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Utilidades de color de la pantalla de apariencia (mismas reglas que valida el servidor)
public static class ColoresApp
{
    public const double ContrasteMinimo = 3.0;

    // "#RRGGBB" en mayúsculas, o null si no es un color válido (se acepta sin el #)
    public static string? Normalizar(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 6 && !t.StartsWith('#')) t = "#" + t;
        if (t.Length != 7 || t[0] != '#') return null;
        return t[1..].All(Uri.IsHexDigit) ? t.ToUpperInvariant() : null;
    }

    public static double Contraste(string a, string b) => ColorHsv.Contraste(a, b);

    public static readonly IReadOnlyDictionary<string, string> Paleta = new Dictionary<string, string>
    {
        ["Terracota"] = "#B5542B", ["Ladrillo"] = "#9C3D2A", ["Ocre"] = "#C9892B", ["Arena"] = "#D9B38C",
        ["Azul"] = "#1B5E9B", ["Azul oscuro"] = "#0D47A1", ["Celeste"] = "#0288D1",
        ["Verde"] = "#2E7D32", ["Verde azulado"] = "#00796B", ["Violeta"] = "#6A1B9A",
        ["Bordó"] = "#AD1457", ["Rojo"] = "#C62828", ["Naranja"] = "#E65100",
        ["Gris azulado"] = "#455A64", ["Negro"] = "#212121", ["Dorado"] = "#FFB300",
        ["Amarillo"] = "#FDD835", ["Blanco"] = "#FFFFFF"
    };
}

// Pantalla del administrador para cambiar colores, nombre y logo de la app sin recompilar.
// El ícono del launcher no se puede cambiar desde acá: lo fija Android al instalar la app.
public class AparienciaViewModel : VmBase
{
    private const float LadoDelLogo = 256f;
    private const int PesoMaximoPng = 110 * 1024;

    private readonly AdminApiService _api;
    private readonly IClienteService _clientes;
    private readonly AparienciaService _remota;

    private string _nombre = string.Empty;
    private string _primario = string.Empty;
    private string _texto = string.Empty;
    private string _acento = string.Empty;
    private ImageSource? _logo;
    private string? _logoNuevoBase64;
    private bool _quitarLogo;

    private readonly IFeedback _feedback;

    public AparienciaViewModel(AdminApiService api, IClienteService clientes, AparienciaService remota, IFeedback feedback)
    {
        _feedback = feedback;
        _api = api;
        _clientes = clientes;
        _remota = remota;
    }

    // Cuál de los tres colores se está editando en el selector: 0 principal, 1 texto, 2 acento
    private int _editando;
    private bool _textoAMano;        // el texto se ajusta solo para leerse bien, salvo que la persona lo haya elegido
    private bool _cargando;
    private bool _vistaPreviaPendiente;

    public string Nombre { get => _nombre; set => Set(ref _nombre, value); }
    public string Primario { get => _primario; set { if (Set(ref _primario, value)) Cambio(); } }
    public string Texto { get => _texto; set { if (Set(ref _texto, value)) Cambio(); } }
    public string Acento { get => _acento; set { if (Set(ref _acento, value)) Cambio(); } }
    public ImageSource? Logo { get => _logo; private set => Set(ref _logo, value); }

    public int Editando
    {
        get => _editando;
        set
        {
            if (!Set(ref _editando, value)) return;
            Notificar(nameof(HexEditando)); Notificar(nameof(TituloEditando));
            Notificar(nameof(BordePrincipal)); Notificar(nameof(BordeTexto)); Notificar(nameof(BordeAcento));
        }
    }

    // El color que muestra y modifica el selector (el selector lo enlaza en los dos sentidos)
    public string HexEditando
    {
        get => _editando switch { 0 => Primario, 1 => Texto, _ => Acento };
        set
        {
            // El selector puede devolver el mismo valor que acaba de recibir: eso no es que la persona lo haya elegido
            if (string.Equals(value, HexEditando, StringComparison.OrdinalIgnoreCase)) return;
            if (_editando == 1) { _textoAMano = true; Notificar(nameof(TextoAutomaticoDisponible)); }
            switch (_editando)
            {
                case 0: Primario = value; break;
                case 1: Texto = value; break;
                default: Acento = value; break;
            }
        }
    }

    public string TituloEditando => _editando switch
    {
        0 => "Color principal: cabeceras y botones",
        1 => "Color del texto sobre el principal",
        _ => "Color de acento: indicadores"
    };

    // El borde marca cuál de los tres se está editando
    private static readonly Color Marcado = Color.FromArgb("#3B2A20"), NoMarcado = Color.FromArgb("#DCCFC2");
    public Color BordePrincipal => _editando == 0 ? Marcado : NoMarcado;
    public Color BordeTexto => _editando == 1 ? Marcado : NoMarcado;
    public Color BordeAcento => _editando == 2 ? Marcado : NoMarcado;

    // Si el texto se eligió a mano, se ofrece volver a que se ajuste solo
    public bool TextoAutomaticoDisponible => _textoAMano;

    public void VolverAlTextoAutomatico()
    {
        _textoAMano = false;
        Notificar(nameof(TextoAutomaticoDisponible));
        if (ColoresApp.Normalizar(Primario) is { } p) Texto = ColorHsv.TextoLegible(p);
        if (_editando == 1) Notificar(nameof(HexEditando));
    }

    public Color PrimarioColor => Color.FromArgb(ColoresApp.Normalizar(Primario) ?? "#BDBDBD");
    public Color TextoColor => Color.FromArgb(ColoresApp.Normalizar(Texto) ?? "#BDBDBD");
    public Color AcentoColor => Color.FromArgb(ColoresApp.Normalizar(Acento) ?? "#BDBDBD");

    // Los tres colores son obligatorios en esta pantalla (se guardan todos); si alguno es inválido o el texto
    // no se lee sobre el color principal, se avisa y no se puede guardar.
    public string Aviso
    {
        get
        {
            var p = ColoresApp.Normalizar(Primario);
            var t = ColoresApp.Normalizar(Texto);
            var a = ColoresApp.Normalizar(Acento);
            if (p is null || t is null || a is null) return "Escribí cada color como #RRGGBB (por ejemplo #1B5E9B) o elegilo de la paleta.";
            if (ColoresApp.Contraste(p, t) < ColoresApp.ContrasteMinimo) return "El texto de los botones no se lee bien sobre el color principal.";
            return string.Empty;
        }
    }

    public bool HayAviso => Aviso.Length > 0;
    public bool PuedeGuardar => !HayAviso && !Procesando;

    private void Cambio()
    {
        // Mientras no se haya elegido el texto a mano, se ajusta solo para que se lea bien sobre el principal
        if (!_cargando && !_textoAMano && ColoresApp.Normalizar(Primario) is { } principal)
        {
            var legible = ColorHsv.TextoLegible(principal);
            if (!string.Equals(_texto, legible, StringComparison.OrdinalIgnoreCase)) { _texto = legible; Notificar(nameof(Texto)); }
        }

        Notificar(nameof(PrimarioColor)); Notificar(nameof(TextoColor)); Notificar(nameof(AcentoColor)); Notificar(nameof(HexEditando));
        Notificar(nameof(Aviso)); Notificar(nameof(HayAviso)); Notificar(nameof(PuedeGuardar));
        if (!_cargando) ProgramarVistaPrevia();
    }

    // Mientras se arrastra, toda la app cambia en vivo. Se aplica como mucho unas 15 veces por segundo para que no se trabe.
    private void ProgramarVistaPrevia()
    {
        if (_vistaPreviaPendiente) return;
        _vistaPreviaPendiente = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(65);
            _vistaPreviaPendiente = false;
            AplicarEnVivo();
        });
    }

    private void AplicarEnVivo()
    {
        var p = ColoresApp.Normalizar(Primario);
        var t = ColoresApp.Normalizar(Texto);
        var a = ColoresApp.Normalizar(Acento);
        if (p is null || t is null || a is null) return;

        var actual = _clientes.Actual;
        _clientes.AplicarApariencia(new AparienciaRemota(Nombre, p, t, a, actual.TieneLogo, actual.VersionLogo), soloVistaPrevia: true);
    }

    // Parte de lo que se está usando ahora (el servidor encima del cliente)
    public void Cargar()
    {
        var actual = _clientes.Actual;
        _logoNuevoBase64 = null;
        _quitarLogo = false;
        _cargando = true;
        Nombre = actual.Nombre;
        Primario = actual.Primario;
        Texto = actual.PrimarioTexto;
        Acento = actual.Acento;
        _cargando = false;
        // Si el texto guardado no es el que se elegiría solo, se lo respeta: lo eligió una persona
        _textoAMano = ColoresApp.Normalizar(actual.Primario) is { } p
                      && !string.Equals(actual.PrimarioTexto, ColorHsv.TextoLegible(p), StringComparison.OrdinalIgnoreCase);
        Notificar(nameof(TextoAutomaticoDisponible));
        Editando = 0;
        Cambio();
        Logo = Application.Current?.Resources.TryGetValue("LogoCliente", out var logo) == true ? logo as ImageSource : null;
        Estado = "Mientras elegís, toda la app cambia en vivo. Si salís sin guardar, vuelve a lo anterior.";
    }

    // Elige una imagen del teléfono y la achica a 256 px: así pesa poco y entra en la planilla
    public async Task ElegirLogoAsync()
    {
        try
        {
            var foto = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions { Title = "Elegí el logo" });
            if (foto is null) return;

            var bytes = await ReductorDeImagenes.ReducirAsync(foto, (int)LadoDelLogo, permitirPng: true, calidadJpeg: 80, pesoMaximoPng: PesoMaximoPng);

            _logoNuevoBase64 = Convert.ToBase64String(bytes);
            _quitarLogo = false;
            Logo = ImageSource.FromStream(() => new MemoryStream(bytes));
            Estado = $"Logo listo ({bytes.Length / 1024} KB). Tocá Guardar para aplicarlo.";
        }
        catch (Exception ex)
        {
            await Dialogos.Aviso("No se pudo cargar la imagen", TextosDeError.ImagenNoValida);
        }
    }

    public void QuitarLogo()
    {
        _logoNuevoBase64 = null;
        _quitarLogo = true;
        Logo = ImageSource.FromFile("logo");
        Estado = "Se va a usar el logo original. Tocá Guardar para aplicarlo.";
    }

    // Aplica lo escrito a toda la app sin guardarlo, para ver cómo queda
    public void VistaPrevia()
    {
        var p = ColoresApp.Normalizar(Primario);
        var t = ColoresApp.Normalizar(Texto);
        var a = ColoresApp.Normalizar(Acento);
        if (p is null || t is null || a is null) return;

        AplicarEnVivo();
        Estado = "Así se ve. Si no te gusta, salí sin guardar y vuelve a lo anterior.";
    }

    // Al salir de la pantalla sin guardar, la app vuelve a lo que estaba guardado
    public void DescartarVistaPrevia() => _clientes.AplicarTema();

    public Task GuardarAsync() => ConEspera(async () =>
    {
        Notificar(nameof(PuedeGuardar));
        var r = await _api.GuardarApariencia(Nombre, ColoresApp.Normalizar(Primario), ColoresApp.Normalizar(Texto),
            ColoresApp.Normalizar(Acento), _logoNuevoBase64, _quitarLogo);

        if (!r.Exito) { await _feedback.ErrorAsync(TextosDeError.Para(null, r.Mensaje)); return; }

        await _remota.ActualizarAsync(); // trae lo guardado y lo aplica a toda la app
        Cargar();
        await _feedback.ExitoAsync("Cambios guardados. Ya los ven todos.");
    }).ContinueWith(_ => Notificar(nameof(PuedeGuardar)));

    public async Task RestaurarAsync()
    {
        if (!await Dialogos.Confirmar("¿Volver a los originales?",
                "Se borran los colores, el nombre y el logo que elegiste y la app usa los que trae de fábrica.", "Volver a los originales"))
            return;

        await ConEspera(async () =>
        {
            var r = await _api.GuardarApariencia(null, null, null, null, null, true);
            if (!r.Exito) { await _feedback.ErrorAsync(TextosDeError.Para(null, r.Mensaje)); return; }

            await _remota.ActualizarAsync();
            Cargar();
            await _feedback.ExitoAsync("Volvimos a la apariencia original.");
        });
    }
}
