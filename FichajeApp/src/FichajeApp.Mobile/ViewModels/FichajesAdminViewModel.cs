using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Revisión y corrección de fichajes: entradas sin salida y salidas sin entrada, que además bloquean el cierre
// de la liquidación hasta que se corrigen.
public class FichajesAdminViewModel : VmBase
{
    private const int MaxMotivo = 150;

    private readonly AdminApiService _api;
    private DateTime _desde = DateTime.Today.AddDays(-14);
    private DateTime _hasta = DateTime.Today;
    private bool _soloVencidos;

    public FichajesAdminViewModel(AdminApiService api) => _api = api;

    public ObservableCollection<FichajeIncompleto> Incompletos { get; } = new();

    public DateTime Desde
    {
        get => _desde;
        set => Set(ref _desde, value);
    }

    public DateTime Hasta
    {
        get => _hasta;
        set => Set(ref _hasta, value);
    }

    public bool SoloVencidos
    {
        get => _soloVencidos;
        set => Set(ref _soloVencidos, value);
    }

    public Task BuscarAsync() => ConEspera(async () =>
    {
        Estado = "Cargando...";
        var r = await _api.FichajesIncompletos(Desde, Hasta, SoloVencidos);

        Incompletos.Clear();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        foreach (var f in r.Datos) Incompletos.Add(f);
        Estado = Incompletos.Count == 0 ? "No hay fichajes incompletos en ese período" : $"{Incompletos.Count} para revisar";
    });

    public async Task CorregirAsync(FichajeIncompleto f)
    {
        if (f.Tipo == "SinSalida") await AgregarSalidaAsync(f);
        else await AgregarEntradaAsync(f);
    }

    // Entrada sin salida: se indica a qué hora salió (y, si hace falta, se ajusta la entrada)
    private async Task AgregarSalidaAsync(FichajeIncompleto f)
    {
        var ingresoHora = await Dialogos.Hora($"{f.Empleado}: hora de entrada", f.Ingreso.ToString("HH:mm"));
        if (ingresoHora is null) return;
        var egresoHora = await Dialogos.Hora($"{f.Empleado}: hora de salida");
        if (egresoHora is null) return;

        var ingreso = f.Ingreso.Date + ingresoHora.Value;
        var egreso = ingreso.Date + egresoHora.Value;
        if (egreso <= ingreso) egreso = egreso.AddDays(1); // un turno nocturno termina al día siguiente

        var motivo = await PedirMotivoAsync();
        if (motivo is null) return;

        await Ejecutar(() => _api.CorregirFichaje(f.EntradaId, ingreso, egreso, motivo), "Fichaje corregido");
    }

    // Salida sin entrada: se agrega la entrada que faltó
    private async Task AgregarEntradaAsync(FichajeIncompleto f)
    {
        var hora = await Dialogos.Hora($"{f.Empleado}: hora de entrada (salió {f.Ingreso:dd/MM HH:mm})");
        if (hora is null) return;

        var ingreso = f.Ingreso.Date + hora.Value;
        if (ingreso >= f.Ingreso) ingreso = ingreso.AddDays(-1);

        var motivo = await PedirMotivoAsync();
        if (motivo is null) return;

        await Ejecutar(() => _api.CompletarEntrada(f.EntradaId, ingreso, motivo), "Entrada cargada");
    }

    // Jornada completa que nunca se fichó
    public async Task CargarManualAsync()
    {
        var empleados = await _api.EmpleadosParaFichajes();
        if (!empleados.Exito || empleados.Datos is null) { await Dialogos.Aviso("No se pudo", empleados.Mensaje); return; }

        var nombre = await Dialogos.Elegir("¿De quién?", empleados.Datos.Select(e => e.Nombre));
        if (nombre is null) return;
        var fecha = await Dialogos.Fecha("Día de la jornada", DateTime.Today.AddDays(-1));
        if (fecha is null) return;
        var entrada = await Dialogos.Hora("Hora de entrada");
        if (entrada is null) return;
        var salida = await Dialogos.Hora("Hora de salida");
        if (salida is null) return;

        var ingreso = fecha.Value + entrada.Value;
        var egreso = fecha.Value + salida.Value;
        if (egreso <= ingreso) egreso = egreso.AddDays(1);

        var motivo = await PedirMotivoAsync();
        if (motivo is null) return;

        await Ejecutar(() => _api.CargarFichajeManual(nombre, ingreso, egreso, motivo), "Fichaje cargado");
    }

    private static async Task<string?> PedirMotivoAsync()
    {
        var motivo = await Dialogos.Texto("Motivo", "¿Por qué se corrige? (queda registrado)", maximo: MaxMotivo);
        return string.IsNullOrWhiteSpace(motivo) ? null : motivo;
    }

    private async Task Ejecutar(Func<Task<ResultadoConsulta<Respuesta>>> accion, string exito)
    {
        await ConEspera(async () =>
        {
            var r = await accion();
            if (!r.Exito) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
            Estado = exito;
        });
        await BuscarAsync();
    }
}
