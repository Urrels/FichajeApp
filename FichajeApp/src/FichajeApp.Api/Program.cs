using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FichajeApp.Api.Persistencia;
using FichajeApp.Api.Persistencia.Sql;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Concurrency;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Options;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

ConfiguracionSegura.ExigirSecretoJwt(builder.Configuration, builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));

// Ningún pedido de esta API necesita más de esto (la foto más grande son 400 KB y viaja en base64): un tope evita
// que alguien con sesión mande cuerpos enormes para llenar la memoria
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.Configure<FichajeOptions>(builder.Configuration.GetSection(FichajeOptions.Seccion));

// La conexión con Google Sheets se puede configurar desde la app: se guarda cifrada en esta carpeta del servidor
var carpetaDeDatos = builder.Configuration["Setup:Carpeta"] ?? Path.Combine(builder.Environment.ContentRootPath, "datos");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(carpetaDeDatos, "llaves")));
builder.Services.AddSingleton<AlmacenConexion>();
builder.Services.AddSingleton<IProbadorConexion, ProbadorConexionGoogle>();
builder.Services.AddSingleton<ICreadorDePlanilla, CreadorDePlanillaGoogle>();
builder.Services.AddSingleton<ConexionService>();

// Una sola conexión a Google Sheets, un reloj inyectable (se reemplaza en las pruebas) y un lock por empleado
builder.Services.AddSingleton<GoogleSheetsClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BloqueoPorClave>();

// Dónde viven los datos: con ConnectionStrings:Fichaje se usa la base SQL (SQL Server / Azure SQL);
// sin ella, la planilla de Google Sheets de siempre.
var cadenaSql = builder.Configuration.GetConnectionString("Fichaje");
var usaSql = !string.IsNullOrWhiteSpace(cadenaSql);

if (usaSql)
{
    // Azure SQL serverless se pausa solo: la primera conexión después de un rato puede fallar, por eso se reintenta
    builder.Services.AddDbContextFactory<FichajeDbContext>(o =>
        o.UseSqlServer(cadenaSql, sql => sql.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

    // Decorator: la lista de empleados se cachea unos segundos (se invalida sola al escribir),
    // porque cada pedido autenticado y cada fichaje la consultan.
    builder.Services.AddSingleton<EmpleadosSqlRepositorio>();
    builder.Services.AddSingleton<IEmpleadosRepositorio>(sp => new CachedEmpleadosRepositorio(
        sp.GetRequiredService<EmpleadosSqlRepositorio>(),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<SitiosSqlRepositorio>();
    builder.Services.AddSingleton<ISitiosAdminRepositorio>(sp => sp.GetRequiredService<SitiosSqlRepositorio>());
    builder.Services.AddSingleton<ISitiosRepository>(sp => new CachedSitiosRepository(
        sp.GetRequiredService<SitiosSqlRepositorio>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IOptions<FichajeOptions>>()));
    builder.Services.AddSingleton<IRolesRepositorio, RolesSqlRepositorio>();
    builder.Services.AddSingleton<IBitacoraRepositorio, BitacoraSqlRepositorio>();
    builder.Services.AddSingleton<IHistorialUsuariosRepositorio, HistorialUsuariosSqlRepositorio>();
    builder.Services.AddSingleton<IFichajesRepositorio, FichajesSqlRepositorio>();
    builder.Services.AddSingleton<ILiquidacionRepositorio, LiquidacionSqlRepositorio>();
    builder.Services.AddSingleton<IHorariosRepositorio, HorariosSqlRepositorio>();
    builder.Services.AddSingleton<INotificacionesRepositorio, NotificacionesSqlRepositorio>();
    builder.Services.AddSingleton<IRecorridoRepositorio, RecorridoSqlRepositorio>();
    builder.Services.AddSingleton<IAparienciaRepositorio, AparienciaSqlRepositorio>();
    builder.Services.AddSingleton<IConfiguracionFichajeRepositorio, ConfiguracionFichajeSqlRepositorio>();
    // Migra la base y siembra permisos, roles y el primer administrador antes de que arranque el resto
    builder.Services.AddHostedService<InicializadorSql>();
    // ConexionService (la pantalla de conectar la planilla) sigue registrado pero no hace nada con SQL
    builder.Services.AddSingleton<FichajeApp.Api.Infrastructure.IInicializadorPlanilla, InicializadorNulo>();
}
else
{
    // Servicios de datos (Google Sheets). Decorator: la lista de empleados se cachea unos segundos
    // (se invalida sola al escribir), porque cada pedido autenticado y cada fichaje la consultan.
    builder.Services.AddSingleton<GoogleSheetsEmpleadosService>();
    builder.Services.AddSingleton<IEmpleadosRepositorio>(sp => new CachedEmpleadosRepositorio(
        sp.GetRequiredService<GoogleSheetsEmpleadosService>(),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<GoogleSheetsSitiosService>();
    builder.Services.AddSingleton<ISitiosRepository>(sp => new CachedSitiosRepository(
        sp.GetRequiredService<GoogleSheetsSitiosService>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IOptions<FichajeOptions>>()));

    builder.Services.AddSingleton<ISitiosAdminRepositorio, SitiosAdminNoDisponible>();
    builder.Services.AddSingleton<IHorariosRepositorio, HorariosNoDisponible>();
    builder.Services.AddSingleton<INotificacionesRepositorio, NotificacionesNoDisponible>();
    builder.Services.AddSingleton<IRecorridoRepositorio, RecorridoNoDisponible>();

    // Repositorios de la administración
    builder.Services.AddSingleton<IRolesRepositorio, RolesSheetsRepositorio>();
    builder.Services.AddSingleton<IBitacoraRepositorio, BitacoraSheetsRepositorio>();
    builder.Services.AddSingleton<IHistorialUsuariosRepositorio, HistorialUsuariosSheetsRepositorio>();
    builder.Services.AddSingleton<IFichajesRepositorio, FichajesSheetsRepositorio>();
    builder.Services.AddSingleton<ILiquidacionRepositorio, LiquidacionSheetsRepositorio>();
    builder.Services.AddSingleton<IAparienciaRepositorio, AparienciaSheetsRepositorio>();
    builder.Services.AddSingleton<IConfiguracionFichajeRepositorio, ConfiguracionFichajeSheetsRepositorio>();
}
builder.Services.AddSingleton<ISitiosService, SitiosService>();
builder.Services.AddSingleton<SitiosAdminService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<SesionesVigentes>();

// Mails (bienvenida y recuperación del PIN) y avisos dentro de la app
builder.Services.Configure<OpcionesCorreo>(builder.Configuration.GetSection(OpcionesCorreo.Seccion));
if (builder.Configuration.GetSection(OpcionesCorreo.Seccion).Get<OpcionesCorreo>()?.Configurado == true)
    builder.Services.AddSingleton<IEnviadorDeCorreo, SmtpEnviador>();
else
    builder.Services.AddSingleton<IEnviadorDeCorreo, EnviadorNulo>();
builder.Services.AddSingleton<NotificacionesService>();
builder.Services.AddSingleton<RecuperacionDePin>();
builder.Services.AddScoped<RecorridoService>();
builder.Services.AddHostedService<LimpiezaRecorridoHostedService>();
builder.Services.AddSingleton<IAlmacenFotos, AlmacenFotosEnDisco>();
builder.Services.AddSingleton<IAlmacenPerfiles, AlmacenPerfilesEnDisco>();
builder.Services.AddSingleton<LimpiezaFotos>();
builder.Services.AddHostedService<LimpiezaFotosHostedService>();

// Servicios de negocio de la administración
builder.Services.AddSingleton<BitacoraService>();
builder.Services.AddSingleton<AparienciaService>();
builder.Services.AddSingleton<ConfiguracionFichajeService>();
builder.Services.AddScoped<KioscoService>();
builder.Services.AddSingleton<PermisosService>();
builder.Services.AddSingleton<GuardaAdministracion>();
builder.Services.AddSingleton<HistorialUsuariosService>();
builder.Services.AddSingleton<LoginService>();
builder.Services.AddSingleton<UsuariosAdminService>();
builder.Services.AddSingleton<RolesService>();
builder.Services.AddSingleton<JornadasService>();
builder.Services.AddSingleton<FichajesAdminService>();
builder.Services.AddSingleton<LiquidacionService>();

// Feriados oficiales (ArgentinaDatos): se sincronizan solos a la pestaña Feriados; "Feriados:SincronizacionAutomatica": false lo apaga
builder.Services.AddHttpClient<IProveedorFeriados, ArgentinaDatosProveedor>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Feriados:Url"] ?? "https://api.argentinadatos.com/");
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<FeriadosService>();
if (builder.Configuration.GetValue("Feriados:SincronizacionAutomatica", true))
    builder.Services.AddHostedService<FeriadosHostedService>();

// Strategy: dónde se guardan los fichajes (SQL o Google Sheets). Con caché del "último tipo del día".
if (usaSql)
{
    builder.Services.AddSingleton<FichajesSqlStorage>();
    builder.Services.AddSingleton<IFichajeStorageStrategy>(sp => new CachedFichajeStorage(
        sp.GetRequiredService<FichajesSqlStorage>(),
        sp.GetRequiredService<IMemoryCache>()));
}
else
{
    // Crea las pestañas y los datos iniciales de la administración al arrancar
    builder.Services.AddSingleton<InicializadorPlanilla>();
    builder.Services.AddSingleton<IInicializadorPlanilla>(sp => sp.GetRequiredService<InicializadorPlanilla>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<InicializadorPlanilla>());

    builder.Services.AddSingleton<GoogleSheetsStorageStrategy>();
    builder.Services.AddSingleton<IFichajeStorageStrategy>(sp => new CachedFichajeStorage(
        sp.GetRequiredService<GoogleSheetsStorageStrategy>(),
        sp.GetRequiredService<IMemoryCache>()));
}

// Observer: se pueden registrar varios observers; todos se inyectan como IEnumerable<IFichajeObserver>
builder.Services.AddSingleton<IFichajeObserver, LogFichajeObserver>();

builder.Services.AddScoped<IFichajeService, FichajeService>();
builder.Services.AddSingleton<ConsultaFichajesService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };

        // Un usuario dado de baja o bloqueado pierde el acceso enseguida, aunque su token siga vigente.
        // El estado se consulta a la planilla como mucho una vez por minuto por usuario.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async contexto =>
            {
                if (!int.TryParse(contexto.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
                {
                    contexto.Fail("Token sin identificador de usuario");
                    return;
                }

                var servicios = contexto.HttpContext.RequestServices;
                var sesion = await servicios.GetRequiredService<SesionesVigentes>().ObtenerAsync(id, async () =>
                {
                    var empleado = await servicios.GetRequiredService<IEmpleadosRepositorio>().ObtenerPorIdAsync(id);
                    if (empleado is not { Activo: true, Bloqueado: false }) return SesionVigente.Denegada;
                    var (_, permisos) = await servicios.GetRequiredService<PermisosService>().ObtenerAsync(empleado);
                    return new SesionVigente(true, permisos.ToHashSet());
                });

                if (!sesion.PuedeEntrar)
                {
                    contexto.Fail("Usuario dado de baja o bloqueado");
                    return;
                }

                // Los permisos viajan dentro del token: si la persona perdió alguno desde que lo recibió, el token ya no vale
                // y tiene que volver a iniciar sesión (la app lo toma como sesión vencida). Ganar permisos no corta nada.
                var delToken = contexto.Principal!.FindAll(ClaimsPermisos.Permiso).Select(c => c.Value);
                if (delToken.Any(p => !sesion.Permisos.Contains(p)))
                    contexto.Fail("Los permisos de este usuario cambiaron: hay que volver a iniciar sesión");
            }
        };
    });

// Autorización por permiso: [RequierePermiso("...")] crea la política a pedido
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermisoPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermisoHandler>();
builder.Services.AddAuthorization();

// Límite de intentos de inicio de sesión por IP (detrás de un proxy hay que configurar ForwardedHeaders)
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.AddPolicy("login", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    // El kiosco ficha a nombre de otras personas con su PIN: se limita por cuenta de sucursal e IP
    opciones.AddPolicy("kiosco", contexto => RateLimitPartition.GetFixedWindowLimiter(
        (contexto.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "-") + "|" + (contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida"),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    opciones.OnRejected = async (contexto, token) =>
    {
        contexto.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await contexto.HttpContext.Response.WriteAsJsonAsync(new { mensaje = "Demasiados intentos. Esperá un minuto." }, token);
    };
});

// Detrás de un proxy (Caddy, Railway, Render...) la IP real del usuario viene en X-Forwarded-For. Solo se activa
// con Proxy:Confiar = true y si la API NO es accesible directamente (si no, cualquiera podría falsear su IP).
if (builder.Configuration.GetValue<bool>("Proxy:Confiar"))
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Proxy:Confiar"))
    app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Sin conexión configurada, cualquier pedido que necesite la planilla responde 503 con un mensaje claro
app.Use(async (contexto, siguiente) =>
{
    try { await siguiente(); }
    catch (ConexionNoConfiguradaException ex) when (!contexto.Response.HasStarted)
    {
        contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await contexto.Response.WriteAsJsonAsync(new { mensaje = ex.Message, codigo = "SIN_CONFIGURAR" });
    }
});

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    app.UseHsts();

// Cabeceras de seguridad en todas las respuestas (la API solo devuelve JSON e imágenes)
app.Use(async (contexto, siguiente) =>
{
    var h = contexto.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await siguiente();
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();   // después de autenticar: el límite del kiosco cuenta por cuenta de sucursal
app.UseAuthorization();
app.MapControllers();

// El código de activación es solo para conectar una planilla de Google: con base SQL no hay nada que conectar
if (!usaSql && !app.Environment.IsEnvironment("Testing") && !app.Services.GetRequiredService<GoogleSheetsClient>().Configurado)
    app.Services.GetRequiredService<ConexionService>().AnunciarClave();

app.Run();

// Necesario para que los tests de integración (WebApplicationFactory) puedan levantar la API
public partial class Program { }
