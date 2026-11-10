using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.Views;

[QueryProperty(nameof(EmpleadoId), "id")]
[QueryProperty(nameof(NombreEmpleado), "nombre")]
public partial class HorarioPage : ContentPage
{
    // Lunes a domingo (DayOfWeek: 1..6 y 0)
    private static readonly (int Dia, string Texto)[] SemanaLaboral =
    {
        (1, "Lunes"), (2, "Martes"), (3, "Miércoles"), (4, "Jueves"), (5, "Viernes"), (6, "Sábado"), (0, "Domingo")
    };

    private readonly AdminApiService _api;
    private readonly Dictionary<int, (Switch Trabaja, TimePicker Entrada, TimePicker Salida)> _filas = new();
    private bool _cargado;

    public HorarioPage(AdminApiService api)
    {
        InitializeComponent();
        _api = api;

        foreach (var (dia, texto) in SemanaLaboral)
        {
            var trabaja = new Switch { VerticalOptions = LayoutOptions.Center };
            var entrada = new TimePicker { Time = new TimeSpan(8, 0, 0), Format = "HH:mm", IsEnabled = false };
            var salida = new TimePicker { Time = new TimeSpan(16, 0, 0), Format = "HH:mm", IsEnabled = false };
            trabaja.Toggled += (_, e) => { entrada.IsEnabled = e.Value; salida.IsEnabled = e.Value; };

            var fila = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)), ColumnSpacing = 8 };
            fila.Add(trabaja, 0);
            fila.Add(new Label { Text = texto, VerticalOptions = LayoutOptions.Center }, 1);
            fila.Add(entrada, 2);
            fila.Add(salida, 3);
            Dias.Add(fila);
            _filas[dia] = (trabaja, entrada, salida);
        }
    }

    public string EmpleadoId { get; set; } = "";
    public string NombreEmpleado { get; set; } = "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Nombre.Text = Uri.UnescapeDataString(NombreEmpleado);
        if (_cargado) return;

        var r = await _api.HorarioDe(int.Parse(EmpleadoId));
        if (!r.Exito || r.Datos is null) { Mostrar(r.Mensaje); return; }

        foreach (var d in r.Datos)
        {
            if (!_filas.TryGetValue(d.Dia, out var fila)) continue;
            fila.Trabaja.IsToggled = true;
            if (TimeSpan.TryParse(d.Entrada, out var entrada)) fila.Entrada.Time = entrada;
            if (TimeSpan.TryParse(d.Salida, out var salida)) fila.Salida.Time = salida;
        }
        _cargado = true;
    }

    private static string Hhmm(TimeSpan? t) { var v = t ?? TimeSpan.Zero; return $"{v.Hours:00}:{v.Minutes:00}"; }

    private void Mostrar(string mensaje)
    {
        Aviso.Texto = mensaje;
        Aviso.IsVisible = true;
    }

    private async void OnGuardar(object sender, EventArgs e)
    {
        var dias = _filas.Where(x => x.Value.Trabaja.IsToggled)
            .Select(x => new HorarioDia { Dia = x.Key, Entrada = Hhmm(x.Value.Entrada.Time), Salida = Hhmm(x.Value.Salida.Time) })
            .ToList();

        Aviso.IsVisible = false;
        GuardarButton.IsEnabled = false;
        Espera.IsVisible = Espera.IsRunning = true;
        var r = await _api.GuardarHorario(int.Parse(EmpleadoId), dias);
        Espera.IsVisible = Espera.IsRunning = false;
        GuardarButton.IsEnabled = true;

        if (!r.Exito) { Mostrar(r.Mensaje); return; }
        await Dialogos.Aviso("Listo", "El horario quedó guardado.");
        await Shell.Current.GoToAsync("..");
    }
}
