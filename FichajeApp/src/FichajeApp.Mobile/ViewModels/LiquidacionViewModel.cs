using System.Collections.ObjectModel;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Liquidación de horas: calcular un período, revisarlo por empleado, cerrarlo y marcarlo como pagado.
public class LiquidacionViewModel : VmBase
{
    private readonly AdminApiService _api;
    private DateTime _desde;
    private DateTime _hasta;
    private ResultadoCalculo? _calculo;
    private string _total = string.Empty;

    private readonly IFeedback _feedback;

    public LiquidacionViewModel(AdminApiService api, IFeedback feedback)
    {
        _api = api;
        _feedback = feedback;

        // Hasta hoy, y desde el 1 del mes; al abrir la pantalla se ajusta a "después de la última liquidación"
        _hasta = DateTime.Today;
        _desde = new DateTime(_hasta.Year, _hasta.Month, 1);
    }

    // Se puede liquidar cuando se quiera: el período sugerido empieza el día siguiente a la última liquidación
    public async Task SugerirPeriodoAsync()
    {
        var r = await _api.Liquidaciones();
        if (!r.Exito || r.Datos is null || r.Datos.Count == 0) return;

        var siguiente = r.Datos.Max(l => l.Hasta).Date.AddDays(1);
        if (siguiente <= DateTime.Today)
        {
            Desde = siguiente;
            Hasta = DateTime.Today;
        }
    }

    public ObservableCollection<ResumenEmpleadoItem> Resumen { get; } = new();

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

    public string Total
    {
        get => _total;
        private set => Set(ref _total, value);
    }

    public Task CalcularAsync() => ConEspera(async () =>
    {
        Estado = "Calculando...";
        var r = await _api.Calcular(Desde, Hasta);

        Resumen.Clear();
        Total = string.Empty;
        _calculo = null;
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        _calculo = r.Datos;
        foreach (var e in r.Datos.Resumen) Resumen.Add(e);
        Total = r.Datos.Resumen.Count == 0 ? "" : "Total a pagar: " + Formato.Dinero(r.Datos.Resumen.Sum(e => e.Importe));
        Estado = Resumen.Count == 0 ? "No hay horas en ese período" : $"{Resumen.Count} empleado(s)";
    });

    // Días de un empleado; tocar uno con un excedente pendiente permite aprobarlo
    public async Task VerDiasAsync(ResumenEmpleadoItem empleado)
    {
        if (_calculo is null) return;

        var dias = _calculo.Dias.Where(d => d.EmpleadoId == empleado.EmpleadoId && d.Empleado == empleado.Empleado)
            .OrderBy(d => d.Fecha).ToList();
        var lineas = dias.ToDictionary(d => d.Linea, d => d);

        var elegida = await Dialogos.Elegir($"{empleado.Empleado} · {empleado.ImporteTexto}", lineas.Keys);
        if (elegida is null) return;

        var dia = lineas[elegida];
        if (dia.Observacion?.Contains("Excedente a aprobar") == true
            && await Dialogos.Confirmar("Horas de más", $"{dia.Empleado} fichó de más el {dia.Fecha:dd/MM}. ¿Querés que se le pague? Si no lo aprobás, esas horas no se suman al pago.", "Aprobar pago"))
        {
            var r = await _api.AprobarExcedente(dia.Empleado, dia.Fecha);
            if (!r.Exito) await Dialogos.Aviso("No se pudo", r.Mensaje);
            else await CalcularAsync();
        }
    }

    public async Task CerrarAsync()
    {
        if (_calculo is null || Resumen.Count == 0)
        {
            await Dialogos.Aviso("Cerrar período", "Primero calculá el período para revisarlo.");
            return;
        }

        var ok = await Dialogos.Confirmar("Cerrar período",
            $"Del {Desde:dd/MM/yyyy} al {Hasta:dd/MM/yyyy}\n{Total}\n\nAl cerrarlo se guarda una copia que ya no se recalcula y no se podrán corregir fichajes de esas fechas.",
            "Cerrar");
        if (!ok) return;

        await ConEspera(async () =>
        {
            var r = await _api.CerrarPeriodo(Desde, Hasta);
            if (!r.Exito) { await Dialogos.Aviso("No se pudo cerrar", r.Mensaje); return; }
            Estado = "Período cerrado";
            Resumen.Clear();
            Total = string.Empty;
            _calculo = null;
        });
    }

    public async Task VerLiquidacionesAsync()
    {
        var r = await _api.Liquidaciones();
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
        if (r.Datos.Count == 0) { await Dialogos.Aviso("Liquidaciones", "Todavía no se cerró ningún período."); return; }

        var lineas = r.Datos.ToDictionary(l => l.Texto, l => l);
        var elegida = await Dialogos.Elegir("Liquidaciones cerradas", lineas.Keys);
        if (elegida is null) return;

        var liquidacion = lineas[elegida];
        var opciones = new List<string> { "Ver resumen", "Compartir CSV" };
        if (liquidacion.Estado == "Cerrada") opciones.Add("Marcar como pagada");

        switch (await Dialogos.Elegir(liquidacion.Texto, opciones))
        {
            case "Ver resumen": await ResumenDeAsync(liquidacion); break;
            case "Compartir CSV": await CompartirCsvAsync(liquidacion); break;
            case "Marcar como pagada":
                if (await Dialogos.Confirmar("¿Marcar como pagada?", "Una vez marcada, ya no se puede volver atrás.", "Marcar como pagada"))
                {
                    var p = await _api.MarcarPagada(liquidacion.Id);
                    await _feedback.ResultadoAsync(p.Exito, "Liquidación marcada como pagada.", p.Mensaje);
                }
                break;
        }
    }

    private async Task ResumenDeAsync(PeriodoLiquidadoItem liquidacion)
    {
        var r = await _api.DetalleLiquidacion(liquidacion.Id);
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }

        var texto = string.Join("\n", r.Datos.Resumen.Select(e => $"{e.Empleado}: {e.ImporteTexto} ({Formato.Horas(e.MinPagables)} h)"))
            + $"\n\nTotal: {Formato.Dinero(r.Datos.Resumen.Sum(e => e.Importe))}";
        await Dialogos.Aviso(liquidacion.Texto, texto);
    }

    private async Task CompartirCsvAsync(PeriodoLiquidadoItem liquidacion)
    {
        var r = await _api.DescargarCsv(liquidacion.Id);
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Liquidación {liquidacion.Desde:dd-MM}-{liquidacion.Hasta:dd-MM}",
            File = new ShareFile(r.Datos)
        });
    }

    // ---- Configuración ----

    public async Task ConfiguracionAsync()
    {
        switch (await Dialogos.Elegir("Configuración", new[] { "Parámetros de cálculo", "Valor hora por empleado", "Feriados" }))
        {
            case "Parámetros de cálculo": await ParametrosAsync(); break;
            case "Valor hora por empleado": await ValorHoraAsync(); break;
            case "Feriados": await FeriadosAsync(); break;
        }
    }

    private async Task ParametrosAsync()
    {
        var r = await _api.Parametros();
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
        var p = r.Datos;

        // Cada línea edita un parámetro; el cambio se guarda enseguida
        var lineas = new Dictionary<string, Action<decimal>>
        {
            [$"Redondeo (min): {p.RedondeoMinutos}"] = v => p.RedondeoMinutos = (int)v,
            [$"Jornada normal (horas): {p.JornadaNormalHoras}"] = v => p.JornadaNormalHoras = v,
            [$"Recargo hora extra (%): {p.RecargoExtraPct}"] = v => p.RecargoExtraPct = v,
            [$"Recargo domingo/feriado (%): {p.RecargoFeriadoPct}"] = v => p.RecargoFeriadoPct = v,
            [$"Tolerancia con turnos (min): {p.ToleranciaMinutos}"] = v => p.ToleranciaMinutos = (int)v,
            [$"Máx. horas de entrada abierta: {p.MaxHorasFichajeAbierto}"] = v => p.MaxHorasFichajeAbierto = (int)v
        };

        var puentes = $"Recargo en puentes y no laborables: {(p.PagarPuentes ? "sí" : "no")}";
        var elegida = await Dialogos.Elegir("Parámetros (tocá uno para cambiarlo)", lineas.Keys.Append(puentes));
        if (elegida is null) return;

        if (elegida == puentes)
        {
            const string Si = "Sí, pagar el recargo", No = "No, solo feriados obligatorios";
            var decision = await Dialogos.Elegir("Puentes y no laborables: ¿suman recargo de feriado al trabajarlos?", new[] { Si, No });
            if (decision is null) return;
            p.PagarPuentes = decision == Si;
            var cambio = await _api.GuardarParametros(p);
            await _feedback.ResultadoAsync(cambio.Exito, "Parámetro guardado.", cambio.Mensaje);
            return;
        }

        var valor = await Dialogos.Numero("Parámetro", elegida.Split(':')[0]);
        if (valor is null) return;

        lineas[elegida](valor.Value);
        var guardado = await _api.GuardarParametros(p);
        await _feedback.ResultadoAsync(guardado.Exito, "Parámetro guardado.", guardado.Mensaje);
    }

    private async Task ValorHoraAsync()
    {
        var r = await _api.ValoresHora();
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }

        const string Nuevo = "➕ Cargar un valor nuevo";
        var opciones = new List<string> { Nuevo };
        opciones.AddRange(r.Datos.Select(v => v.Texto));

        if (await Dialogos.Elegir("Valor hora (el vigente se elige por fecha)", opciones) != Nuevo) return;

        var empleados = await _api.EmpleadosParaLiquidacion();
        if (!empleados.Exito || empleados.Datos is null) { await Dialogos.Aviso("No se pudo", empleados.Mensaje); return; }

        var nombre = await Dialogos.Elegir("¿Para quién?", empleados.Datos.Select(e => e.Nombre));
        if (nombre is null) return;
        var valor = await Dialogos.Numero("Valor hora", $"Valor de la hora de {nombre} ($)");
        if (valor is null) return;
        var desde = await Dialogos.Fecha("Vigente desde", DateTime.Today);
        if (desde is null) return;

        var empleado = empleados.Datos.First(e => e.Nombre == nombre);
        var guardado = await _api.GuardarValorHora(empleado.Id, valor.Value, desde.Value);
        await _feedback.ResultadoAsync(guardado.Exito, "Valor hora guardado.", guardado.Mensaje);
    }

    private async Task FeriadosAsync()
    {
        var anio = DateTime.Today.Year;
        var r = await _api.Feriados(new DateTime(anio, 1, 1), new DateTime(anio + 1, 12, 31));
        if (!r.Exito || r.Datos is null) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }

        const string Agregar = "➕ Agregar un feriado";
        const string Actualizar = "🔄 Actualizar feriados oficiales";
        var lineas = r.Datos.ToDictionary(f => f.Texto, f => f);
        var opciones = new List<string> { Actualizar, Agregar };
        opciones.AddRange(lineas.Keys);

        var elegida = await Dialogos.Elegir("Feriados (se pagan con el recargo de domingo)", opciones);
        if (elegida is null) return;

        if (elegida == Actualizar)
        {
            var sync = await _api.SincronizarFeriados();
            await _feedback.ResultadoAsync(sync.Exito, sync.Datos?.Mensaje ?? "Feriados actualizados.", sync.Mensaje);
        }
        else if (elegida == Agregar)
        {
            var fecha = await Dialogos.Fecha("Feriado", DateTime.Today);
            if (fecha is null) return;
            var descripcion = await Dialogos.Texto("Feriado", "Descripción", maximo: 100);
            if (string.IsNullOrEmpty(descripcion)) return;

            var guardado = await _api.GuardarFeriado(fecha.Value, descripcion);
            await _feedback.ResultadoAsync(guardado.Exito, "Feriado guardado.", guardado.Mensaje);
        }
        else if (await Dialogos.Confirmar("¿Eliminar este feriado?", $"{elegida}\n\nDejará de sumar el recargo en las liquidaciones nuevas.", "Eliminar feriado"))
        {
            var eliminado = await _api.EliminarFeriado(lineas[elegida].Fecha);
            await _feedback.ResultadoAsync(eliminado.Exito, "Feriado eliminado.", eliminado.Mensaje);
        }
    }
}
