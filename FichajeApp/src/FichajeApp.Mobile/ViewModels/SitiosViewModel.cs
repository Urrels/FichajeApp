using System.Collections.ObjectModel;
using System.Globalization;
using FichajeApp.Mobile.Services;

namespace FichajeApp.Mobile.ViewModels;

// Lugares conocidos: donde se puede fichar con "Ubicación libre". Fuera de ellos la persona tiene que escribir un motivo.
public class SitiosViewModel : VmBase
{
    private const double RadioPorDefecto = 100;
    private const double PrecisionAceptada = 30;   // para dar de alta un lugar se pide una lectura buena, no solo "alcanzable"

    private readonly AdminApiService _api;
    private readonly IGeolocationService _geo;

    public SitiosViewModel(AdminApiService api, IGeolocationService geo)
    {
        _api = api;
        _geo = geo;
    }

    public ObservableCollection<SitioAdminItem> Sitios { get; } = new();

    public Task CargarAsync() => ConEspera(async () =>
    {
        Estado = "Cargando...";
        var r = await _api.ListarSitios();

        Sitios.Clear();
        if (!r.Exito || r.Datos is null) { Estado = r.Mensaje; return; }

        foreach (var s in r.Datos) Sitios.Add(s);
        Estado = r.Datos.Count == 0 ? "Todavía no cargaste ningún lugar" : $"{r.Datos.Count} lugar(es)";
    });

    public async Task AgregarAsync()
    {
        var punto = await ElegirUbicacionAsync("Nuevo lugar");
        if (punto is null) return;

        var nombre = await Dialogos.Texto("Nuevo lugar", "Nombre (ej. Sede central, Depósito)", maximo: 100);
        if (string.IsNullOrEmpty(nombre)) return;
        var radio = await PedirRadioAsync(RadioPorDefecto);
        if (radio is null) return;

        await Ejecutar(() => _api.CrearSitio(nombre, punto.Value.Lat, punto.Value.Lon, radio.Value), "Lugar guardado");
    }

    public async Task AccionesAsync(SitioAdminItem sitio)
    {
        var elegida = await Dialogos.Elegir(sitio.Nombre,
            new[] { "Cambiar el nombre", "Cambiar el radio", "Mover a mi ubicación actual", "Ver en el mapa" }, destructiva: "Eliminar");
        switch (elegida)
        {
            case "Cambiar el nombre":
                var nombre = await Dialogos.Texto("Nombre del lugar", "Nombre", sitio.Nombre, 100);
                if (!string.IsNullOrEmpty(nombre))
                    await Ejecutar(() => _api.ActualizarSitio(sitio.Id, nombre, sitio.Latitud, sitio.Longitud, sitio.RadioMetros), "Lugar guardado");
                break;

            case "Cambiar el radio":
                var radio = await PedirRadioAsync(sitio.RadioMetros);
                if (radio is not null)
                    await Ejecutar(() => _api.ActualizarSitio(sitio.Id, sitio.Nombre, sitio.Latitud, sitio.Longitud, radio.Value), "Lugar guardado");
                break;

            case "Mover a mi ubicación actual":
                var punto = await UbicacionActualAsync();
                if (punto is not null)
                    await Ejecutar(() => _api.ActualizarSitio(sitio.Id, sitio.Nombre, punto.Value.Lat, punto.Value.Lon, sitio.RadioMetros), "Lugar guardado");
                break;

            case "Ver en el mapa":
                try
                {
                    var enlace = string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={sitio.Latitud},{sitio.Longitud}");
                    await Launcher.Default.OpenAsync(new Uri(enlace));
                }
                catch { await Dialogos.Aviso("No se pudo", "No se pudo abrir el mapa."); }
                break;

            case "Eliminar":
                if (await Dialogos.Confirmar("Eliminar lugar", $"¿Eliminar «{sitio.Nombre}»? Quien fiche ahí va a tener que escribir un motivo.", "Eliminar"))
                    await Ejecutar(() => _api.EliminarSitio(sitio.Id), "Lugar eliminado");
                break;
        }
    }

    // El lugar se marca parándose en él (lo más cómodo y preciso) o escribiendo las coordenadas
    private async Task<(double Lat, double Lon)?> ElegirUbicacionAsync(string titulo)
    {
        const string AhoraMismo = "Estoy en el lugar ahora (usar mi ubicación)";
        const string Escribir = "Escribir latitud y longitud";

        var origen = await Dialogos.Elegir($"{titulo}: ¿dónde queda?", new[] { AhoraMismo, Escribir });
        if (origen == AhoraMismo) return await UbicacionActualAsync();
        if (origen != Escribir) return null;

        var lat = await Dialogos.Numero(titulo, "Latitud (ej. -34.603722)");
        if (lat is null) return null;
        var lon = await Dialogos.Numero(titulo, "Longitud (ej. -58.381592)");
        if (lon is null) return null;
        return ((double)lat.Value, (double)lon.Value);
    }

    private async Task<(double Lat, double Lon)?> UbicacionActualAsync()
    {
        Procesando = true;
        Estado = "Buscando tu ubicación... (mejor al aire libre)";
        try
        {
            var u = await _geo.ObtenerUbicacionAsync(PrecisionAceptada);
            if (!u.Exito) { await Dialogos.Aviso("No se pudo", u.Mensaje); return null; }
            if (u.EsFalsa) { await Dialogos.Aviso("No se pudo", "Se detectó una ubicación simulada. Desactivá las apps de GPS falso."); return null; }
            if (u.Precision > 100)
            {
                var sigue = await Dialogos.Confirmar("Ubicación poco precisa",
                    $"La ubicación tiene un error de ±{u.Precision:0} m. Si guardás el lugar así puede quedar corrido. ¿Usarla igual?", "Usarla");
                if (!sigue) return null;
            }
            return (u.Lat, u.Lon);
        }
        finally { Procesando = false; Estado = string.Empty; }
    }

    private static async Task<double?> PedirRadioAsync(double inicial)
    {
        var radio = await Dialogos.Numero("Radio del lugar", "Hasta cuántos metros del punto se considera que está en el lugar (10 a 5000)",
            inicial.ToString("0", CultureInfo.InvariantCulture));
        return radio is null ? null : (double)radio.Value;
    }

    private async Task Ejecutar(Func<Task<ResultadoConsulta<Respuesta>>> accion, string exito)
    {
        await ConEspera(async () =>
        {
            var r = await accion();
            if (!r.Exito) { await Dialogos.Aviso("No se pudo", r.Mensaje); return; }
            Estado = exito;
        });
        await CargarAsync();
    }
}
