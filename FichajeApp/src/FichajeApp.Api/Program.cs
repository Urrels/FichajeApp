using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
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
builder.Services.AddSingleton<ISitiosService, SitiosService>();
builder.Services.AddSingleton<JwtService>();

// Repositorios de la administración (detrás de interfaces: hoy Sheets, mañana una base de datos)
builder.Services.AddSingleton<IRolesRepositorio, RolesSheetsRepositorio>();
builder.Services.AddSingleton<IBitacoraRepositorio, BitacoraSheetsRepositorio>();
builder.Services.AddSingleton<IHistorialUsuariosRepositorio, HistorialUsuariosSheetsRepositorio>();
builder.Services.AddSingleton<IFichajesRepositorio, FichajesSheetsRepositorio>();
builder.Services.AddSingleton<ILiquidacionRepositorio, LiquidacionSheetsRepositorio>();
builder.Services.AddSingleton<IAparienciaRepositorio, AparienciaSheetsRepositorio>();
builder.Services.AddSingleton<IConfiguracionFichajeRepositorio, ConfiguracionFichajeSheetsRepositorio>();
builder.Services.AddSingleton<IAlmacenFotos, AlmacenFotosEnDisco>();
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

// Crea las pestañas y los datos iniciales de la administración al arrancar
builder.Services.AddSingleton<InicializadorPlanilla>();
builder.Services.AddSingleton<IInicializadorPlanilla>(sp => sp.GetRequiredService<InicializadorPlanilla>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<InicializadorPlanilla>());

// Strategy: hoy Google Sheets. Para sumar otra, se registra otra implementación acá.
builder.Services.AddSingleton<GoogleSheetsStorageStrategy>();
builder.Services.AddSingleton<IFichajeStorageStrategy>(sp => new CachedFichajeStorage(
    sp.GetRequiredService<GoogleSheetsStorageStrategy>(),
    sp.GetRequiredService<IMemoryCache>()));

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
                var cache = servicios.GetRequiredService<IMemoryCache>();
                var puedeEntrar = await cache.GetOrCreateAsync($"sesion:{id}", async entrada =>
                {
                    entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var empleado = await servicios.GetRequiredService<IEmpleadosRepositorio>().ObtenerPorIdAsync(id);
                    return empleado is { Activo: true, Bloqueado: false };
                });

                if (puedeEntrar != true) contexto.Fail("Usuario dado de baja o bloqueado");
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

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (!app.Environment.IsEnvironment("Testing") && !app.Services.GetRequiredService<GoogleSheetsClient>().Configurado)
    app.Services.GetRequiredService<ConexionService>().AnunciarClave();

app.Run();

// Necesario para que los tests de integración (WebApplicationFactory) puedan levantar la API
public partial class Program { }
