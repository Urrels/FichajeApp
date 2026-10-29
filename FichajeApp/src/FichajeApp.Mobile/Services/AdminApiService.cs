using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FichajeApp.Mobile.Services;

// ---- DTOs (espejo de los de la API) ----

public class Respuesta
{
    public string? Mensaje { get; set; }
    public int? Id { get; set; }
}

public class PaginaDatos<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int Tamanio { get; set; }
}

public class UsuarioAdmin
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool Bloqueado { get; set; }
    public int IntentosFallidos { get; set; }
    public List<int> RolIds { get; set; } = new();
    public List<string> Roles { get; set; } = new();

    public string Estado => !Activo ? "Dado de baja" : Bloqueado ? "Bloqueado" : "Activo";
    public string Detalle => $"{Email} · {string.Join(", ", Roles)} · {Estado}";
}

public class RolResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class NodoRol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? PadreId { get; set; }
    public bool EsRol { get; set; }
    public bool Protegido { get; set; }
    public List<NodoRol> Hijos { get; set; } = new();
}

public class PermisoItem
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class HistorialItem
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string RealizadoPor { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
}

public class EntradaBitacora
{
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;

    public string Texto => $"{FechaHora:dd/MM HH:mm} · {Usuario}";
}

public class EmpleadoResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class FichajeIncompleto
{
    public string Tipo { get; set; } = string.Empty; // SinSalida | SalidaSinEntrada
    public int EntradaId { get; set; }
    public int? SalidaId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public DateTime Ingreso { get; set; }
    public bool Vencida { get; set; }

    public string Resumen => Tipo == "SinSalida"
        ? $"Entró {Ingreso:dd/MM HH:mm} · sin salida" + (Vencida ? " (vencida)" : "")
        : $"Salió {Ingreso:dd/MM HH:mm} · sin entrada";
}

public class LiquidacionDiaItem
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public int MinTrabajados { get; set; }
    public int MinPagables { get; set; }
    public decimal Importe { get; set; }
    public bool Ausente { get; set; }
    public string? Observacion { get; set; }

    public string Linea => $"{Fecha:dd/MM} · {Formato.Horas(MinPagables)} h · {Formato.Dinero(Importe)}"
        + (string.IsNullOrWhiteSpace(Observacion) ? "" : $" · {Observacion}");
}

public class ResumenEmpleadoItem
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public int MinTrabajados { get; set; }
    public int MinPagables { get; set; }
    public int MinExtra { get; set; }
    public int MinFeriado { get; set; }
    public int Ausencias { get; set; }
    public decimal Importe { get; set; }

    public string Detalle => $"{Formato.Horas(MinPagables)} h a pagar (extra {Formato.Horas(MinExtra)} · dom/fer {Formato.Horas(MinFeriado)})"
        + (Ausencias > 0 ? $" · {Ausencias} ausencia(s)" : "");
    public string ImporteTexto => Formato.Dinero(Importe);
}

public class ResultadoCalculo
{
    public List<LiquidacionDiaItem> Dias { get; set; } = new();
    public List<ResumenEmpleadoItem> Resumen { get; set; } = new();
}

public class PeriodoLiquidadoItem
{
    public int Id { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string CerradaPor { get; set; } = string.Empty;
    public string Texto => $"{Desde:dd/MM/yyyy} al {Hasta:dd/MM/yyyy} · {Estado}";
}

public class ParametrosLiquidacionItem
{
    public int ToleranciaMinutos { get; set; }
    public int RedondeoMinutos { get; set; }
    public decimal JornadaNormalHoras { get; set; }
    public decimal RecargoExtraPct { get; set; }
    public decimal RecargoFeriadoPct { get; set; }
    public int MaxHorasFichajeAbierto { get; set; }
}

public class ValorHoraItem
{
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateTime VigenteDesde { get; set; }
    public string Texto => $"{Empleado} · {Formato.Dinero(Valor)} desde {VigenteDesde:yyyy-MM-dd}";
}

public class FeriadoItem
{
    public DateTime Fecha { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Texto => $"{Fecha:yyyy-MM-dd} · {Descripcion}";
}

public static class Formato
{
    private static readonly CultureInfo Ar = new("es-AR");

    public static string Horas(int minutos) => (minutos / 60m).ToString("0.##", Ar);
    public static string Dinero(decimal valor) => "$ " + valor.ToString("N2", Ar);
    public static string Fecha(DateTime f) => f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// ---- Servicio ----

// Cliente de los endpoints de administración. Cada método es un endpoint: los ViewModels no conocen rutas.
// Los errores del servidor ({ mensaje }) llegan ya como texto para mostrar al usuario.
public class AdminApiService
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http = new();
    private readonly AuthService _auth;
    private readonly IClienteService _clientes;

    public AdminApiService(AuthService auth, IClienteService clientes)
    {
        _auth = auth;
        _clientes = clientes;
    }

    // ---- Usuarios ----
    public Task<ResultadoConsulta<PaginaDatos<UsuarioAdmin>>> ListarUsuarios(string? busqueda, int pagina = 1, int tamanio = 100) =>
        Pedir<PaginaDatos<UsuarioAdmin>>(HttpMethod.Get, $"admin/usuarios?pagina={pagina}&tamanio={tamanio}" +
            (string.IsNullOrWhiteSpace(busqueda) ? "" : $"&busqueda={Uri.EscapeDataString(busqueda)}"));
    public Task<ResultadoConsulta<List<RolResumen>>> ListarRolesParaAsignar() => Pedir<List<RolResumen>>(HttpMethod.Get, "admin/usuarios/roles");
    public Task<ResultadoConsulta<Respuesta>> CrearUsuario(string nombre, string email, string pin, List<int> rolIds) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/usuarios", new { nombreCompleto = nombre, email, pin, rolIds });
    public Task<ResultadoConsulta<Respuesta>> EditarUsuario(int id, string nombre, string email) =>
        Pedir<Respuesta>(HttpMethod.Put, $"admin/usuarios/{id}/datos", new { nombreCompleto = nombre, email });
    public Task<ResultadoConsulta<Respuesta>> DarDeBaja(int id) => Pedir<Respuesta>(HttpMethod.Post, $"admin/usuarios/{id}/baja");
    public Task<ResultadoConsulta<Respuesta>> Reactivar(int id) => Pedir<Respuesta>(HttpMethod.Post, $"admin/usuarios/{id}/reactivar");
    public Task<ResultadoConsulta<Respuesta>> Desbloquear(int id) => Pedir<Respuesta>(HttpMethod.Post, $"admin/usuarios/{id}/desbloquear");
    public Task<ResultadoConsulta<Respuesta>> RestablecerPin(int id, string pin) =>
        Pedir<Respuesta>(HttpMethod.Post, $"admin/usuarios/{id}/restablecer-pin", new { pin });
    public Task<ResultadoConsulta<Respuesta>> AsignarRoles(int id, List<int> rolIds) =>
        Pedir<Respuesta>(HttpMethod.Put, $"admin/usuarios/{id}/roles", new { rolIds });
    public Task<ResultadoConsulta<List<HistorialItem>>> HistorialDeUsuario(int id) =>
        Pedir<List<HistorialItem>>(HttpMethod.Get, $"admin/usuarios/{id}/historial");
    public Task<ResultadoConsulta<Respuesta>> Rollback(int historialId) =>
        Pedir<Respuesta>(HttpMethod.Post, $"admin/usuarios/historial/{historialId}/rollback");

    // ---- Cuenta propia ----
    public Task<ResultadoConsulta<Respuesta>> CambiarMiPin(string actual, string nuevo) =>
        Pedir<Respuesta>(HttpMethod.Post, "cuenta/cambiar-pin", new { pinActual = actual, pinNuevo = nuevo });

    // ---- Roles ----
    public Task<ResultadoConsulta<List<NodoRol>>> ArbolDeRoles() => Pedir<List<NodoRol>>(HttpMethod.Get, "admin/roles/arbol");
    public Task<ResultadoConsulta<List<PermisoItem>>> CatalogoDePermisos() => Pedir<List<PermisoItem>>(HttpMethod.Get, "admin/roles/permisos");
    public Task<ResultadoConsulta<List<int>>> PermisosDeRol(int rolId) => Pedir<List<int>>(HttpMethod.Get, $"admin/roles/{rolId}/permisos");
    public Task<ResultadoConsulta<Respuesta>> CrearRol(string nombre, int? padreId) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/roles", new { nombre, padreId });
    public Task<ResultadoConsulta<Respuesta>> DuplicarRol(int rolId, string nombre) =>
        Pedir<Respuesta>(HttpMethod.Post, $"admin/roles/{rolId}/duplicar", new { nombre });
    public Task<ResultadoConsulta<Respuesta>> CambiarPadre(int rolId, int? padreId) =>
        Pedir<Respuesta>(HttpMethod.Put, $"admin/roles/{rolId}/padre", new { padreId });
    public Task<ResultadoConsulta<Respuesta>> GuardarPermisos(int rolId, List<int> permisoIds) =>
        Pedir<Respuesta>(HttpMethod.Put, $"admin/roles/{rolId}/permisos", new { permisoIds });
    public Task<ResultadoConsulta<Respuesta>> EliminarRol(int rolId) => Pedir<Respuesta>(HttpMethod.Delete, $"admin/roles/{rolId}");

    // ---- Bitácora ----
    public Task<ResultadoConsulta<PaginaDatos<EntradaBitacora>>> Bitacora(string? usuario, string? accion, int pagina, int tamanio = 30) =>
        Pedir<PaginaDatos<EntradaBitacora>>(HttpMethod.Get, $"admin/bitacora?pagina={pagina}&tamanio={tamanio}" +
            (string.IsNullOrWhiteSpace(usuario) ? "" : $"&usuario={Uri.EscapeDataString(usuario)}") +
            (string.IsNullOrWhiteSpace(accion) ? "" : $"&accion={Uri.EscapeDataString(accion)}"));

    // ---- Corrección de fichajes ----
    public Task<ResultadoConsulta<List<FichajeIncompleto>>> FichajesIncompletos(DateTime desde, DateTime hasta, bool soloVencidos) =>
        Pedir<List<FichajeIncompleto>>(HttpMethod.Get,
            $"admin/fichajes/incompletos?desde={Formato.Fecha(desde)}&hasta={Formato.Fecha(hasta)}&soloVencidos={soloVencidos.ToString().ToLowerInvariant()}");
    public Task<ResultadoConsulta<List<EmpleadoResumen>>> EmpleadosParaFichajes() => Pedir<List<EmpleadoResumen>>(HttpMethod.Get, "admin/fichajes/empleados");
    public Task<ResultadoConsulta<Respuesta>> CorregirFichaje(int entradaId, DateTime ingreso, DateTime egreso, string motivo) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/fichajes/corregir", new { entradaId, ingreso, egreso, motivo });
    public Task<ResultadoConsulta<Respuesta>> CompletarEntrada(int salidaId, DateTime ingreso, string motivo) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/fichajes/completar-entrada", new { salidaId, ingreso, motivo });
    public Task<ResultadoConsulta<Respuesta>> CargarFichajeManual(string empleado, DateTime ingreso, DateTime egreso, string motivo) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/fichajes/manual", new { empleado, ingreso, egreso, motivo });

    // ---- Apariencia ----
    // Lo que llega vacío vuelve al valor del cliente. El logo solo cambia si se manda uno (base64) o se pide quitarlo.
    public Task<ResultadoConsulta<Respuesta>> GuardarApariencia(string? nombre, string? colorPrimario, string? colorPrimarioTexto,
        string? colorAcento, string? logoBase64, bool quitarLogo) =>
        Pedir<Respuesta>(HttpMethod.Put, "admin/apariencia",
            new { nombre, colorPrimario, colorPrimarioTexto, colorAcento, logoBase64, quitarLogo });

    // ---- Liquidación ----
    public Task<ResultadoConsulta<ResultadoCalculo>> Calcular(DateTime desde, DateTime hasta) =>
        Pedir<ResultadoCalculo>(HttpMethod.Get, $"admin/liquidacion/calcular?desde={Formato.Fecha(desde)}&hasta={Formato.Fecha(hasta)}");
    public Task<ResultadoConsulta<PeriodoLiquidadoItem>> CerrarPeriodo(DateTime desde, DateTime hasta) =>
        Pedir<PeriodoLiquidadoItem>(HttpMethod.Post, "admin/liquidacion/cerrar", new { desde, hasta });
    public Task<ResultadoConsulta<List<PeriodoLiquidadoItem>>> Liquidaciones() =>
        Pedir<List<PeriodoLiquidadoItem>>(HttpMethod.Get, "admin/liquidacion/liquidaciones");
    public Task<ResultadoConsulta<ResultadoCalculo>> DetalleLiquidacion(int id) =>
        Pedir<ResultadoCalculo>(HttpMethod.Get, $"admin/liquidacion/liquidaciones/{id}");
    public Task<ResultadoConsulta<Respuesta>> MarcarPagada(int id) =>
        Pedir<Respuesta>(HttpMethod.Post, $"admin/liquidacion/liquidaciones/{id}/pagar");
    public Task<ResultadoConsulta<Respuesta>> AprobarExcedente(string empleado, DateTime fecha) =>
        Pedir<Respuesta>(HttpMethod.Post, "admin/liquidacion/excedentes/aprobar", new { empleado, fecha });
    public Task<ResultadoConsulta<ParametrosLiquidacionItem>> Parametros() =>
        Pedir<ParametrosLiquidacionItem>(HttpMethod.Get, "admin/liquidacion/parametros");
    public Task<ResultadoConsulta<Respuesta>> GuardarParametros(ParametrosLiquidacionItem p) =>
        Pedir<Respuesta>(HttpMethod.Put, "admin/liquidacion/parametros", p);
    public Task<ResultadoConsulta<List<ValorHoraItem>>> ValoresHora() => Pedir<List<ValorHoraItem>>(HttpMethod.Get, "admin/liquidacion/valor-hora");
    public Task<ResultadoConsulta<Respuesta>> GuardarValorHora(int empleadoId, decimal valor, DateTime vigenteDesde) =>
        Pedir<Respuesta>(HttpMethod.Put, "admin/liquidacion/valor-hora", new { empleadoId, valor, vigenteDesde });
    public Task<ResultadoConsulta<List<EmpleadoResumen>>> EmpleadosParaLiquidacion() =>
        Pedir<List<EmpleadoResumen>>(HttpMethod.Get, "admin/liquidacion/empleados");
    public Task<ResultadoConsulta<List<FeriadoItem>>> Feriados(DateTime desde, DateTime hasta) =>
        Pedir<List<FeriadoItem>>(HttpMethod.Get, $"admin/liquidacion/feriados?desde={Formato.Fecha(desde)}&hasta={Formato.Fecha(hasta)}");
    public Task<ResultadoConsulta<Respuesta>> GuardarFeriado(DateTime fecha, string descripcion) =>
        Pedir<Respuesta>(HttpMethod.Put, "admin/liquidacion/feriados", new { fecha, descripcion });
    public Task<ResultadoConsulta<Respuesta>> EliminarFeriado(DateTime fecha) =>
        Pedir<Respuesta>(HttpMethod.Delete, $"admin/liquidacion/feriados?fecha={Formato.Fecha(fecha)}");

    // Baja el CSV de una liquidación a la carpeta temporal y devuelve la ruta (para compartirlo)
    public async Task<ResultadoConsulta<string>> DescargarCsv(int liquidacionId)
    {
        try
        {
            using var pedido = Crear(HttpMethod.Get, $"admin/liquidacion/liquidaciones/{liquidacionId}/csv", null, out var error);
            if (pedido is null) return new ResultadoConsulta<string>(false, error!, null);

            using var cts = new CancellationTokenSource(Espera);
            using var respuesta = await _http.SendAsync(pedido, cts.Token);
            var fallo = await RevisarErrores(respuesta);
            if (fallo != null) return new ResultadoConsulta<string>(false, fallo, null);

            var ruta = Path.Combine(FileSystem.CacheDirectory, $"liquidacion-{liquidacionId}.csv");
            await File.WriteAllBytesAsync(ruta, await respuesta.Content.ReadAsByteArrayAsync());
            return new ResultadoConsulta<string>(true, "OK", ruta);
        }
        catch (OperationCanceledException) { return new ResultadoConsulta<string>(false, "El servidor no respondió a tiempo", null); }
        catch (Exception ex) { return new ResultadoConsulta<string>(false, $"Error de conexión: {ex.Message}", null); }
    }

    // ---- núcleo ----

    private async Task<ResultadoConsulta<T>> Pedir<T>(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        try
        {
            using var pedido = Crear(metodo, ruta, cuerpo, out var error);
            if (pedido is null) return new ResultadoConsulta<T>(false, error!, default);

            using var cts = new CancellationTokenSource(Espera);
            using var respuesta = await _http.SendAsync(pedido, cts.Token);

            var fallo = await RevisarErrores(respuesta);
            if (fallo != null) return new ResultadoConsulta<T>(false, fallo, default);

            var datos = await respuesta.Content.ReadFromJsonAsync<T>();
            return new ResultadoConsulta<T>(true, "OK", datos);
        }
        catch (OperationCanceledException) { return new ResultadoConsulta<T>(false, "El servidor no respondió a tiempo", default); }
        catch (Exception ex) { return new ResultadoConsulta<T>(false, $"Error de conexión: {ex.Message}", default); }
    }

    // Header por request: el HttpClient es compartido y no se muta
    private HttpRequestMessage? Crear(HttpMethod metodo, string ruta, object? cuerpo, out string? error)
    {
        var token = SessionManager.Instance.Token;
        if (string.IsNullOrEmpty(token))
        {
            _auth.NotificarSesionExpirada();
            error = "Sesión expirada, iniciá sesión de nuevo";
            return null;
        }

        error = null;
        var pedido = new HttpRequestMessage(metodo, $"{_clientes.BaseUrl}/{ruta}");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cuerpo != null) pedido.Content = JsonContent.Create(cuerpo);
        return pedido;
    }

    // Devuelve el mensaje de error para mostrar, o null si la respuesta fue exitosa
    private async Task<string?> RevisarErrores(HttpResponseMessage respuesta)
    {
        if (respuesta.IsSuccessStatusCode) return null;

        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _auth.NotificarSesionExpirada();
            return "Sesión expirada, iniciá sesión de nuevo";
        }
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return "No tenés permiso para esta acción";

        // El error puede venir como JSON { mensaje } o como texto: no asumir el formato
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        try
        {
            var mensaje = JsonDocument.Parse(cuerpo).RootElement.GetProperty("mensaje").GetString();
            if (!string.IsNullOrWhiteSpace(mensaje)) return mensaje;
        }
        catch { /* no era JSON con "mensaje" */ }
        return $"El servidor respondió con error ({(int)respuesta.StatusCode})";
    }
}
