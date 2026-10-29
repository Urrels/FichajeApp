using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using FichajeApp.Api.Autorizacion;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Repositorios;
using FichajeApp.Api.Servicios;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// Una sola conexión a Google Sheets y un reloj inyectable (se reemplaza en las pruebas)
builder.Services.AddSingleton<GoogleSheetsClient>();
builder.Services.AddSingleton(TimeProvider.System);

// Servicios de datos (Google Sheets)
builder.Services.AddSingleton<GoogleSheetsEmpleadosService>();
builder.Services.AddSingleton<IEmpleadosRepositorio>(sp => sp.GetRequiredService<GoogleSheetsEmpleadosService>());
builder.Services.AddSingleton<GoogleSheetsSitiosService>();
builder.Services.AddSingleton<JwtService>();

// Repositorios de la administración (detrás de interfaces: hoy Sheets, mañana una base de datos)
builder.Services.AddSingleton<IRolesRepositorio, RolesSheetsRepositorio>();
builder.Services.AddSingleton<IBitacoraRepositorio, BitacoraSheetsRepositorio>();
builder.Services.AddSingleton<IHistorialUsuariosRepositorio, HistorialUsuariosSheetsRepositorio>();
builder.Services.AddSingleton<IFichajesRepositorio, FichajesSheetsRepositorio>();
builder.Services.AddSingleton<ILiquidacionRepositorio, LiquidacionSheetsRepositorio>();

// Servicios de negocio de la administración
builder.Services.AddSingleton<BitacoraService>();
builder.Services.AddSingleton<PermisosService>();
builder.Services.AddSingleton<GuardaAdministracion>();
builder.Services.AddSingleton<HistorialUsuariosService>();
builder.Services.AddSingleton<LoginService>();
builder.Services.AddSingleton<UsuariosAdminService>();
builder.Services.AddSingleton<RolesService>();
builder.Services.AddSingleton<JornadasService>();
builder.Services.AddSingleton<FichajesAdminService>();
builder.Services.AddSingleton<LiquidacionService>();

// Crea las pestañas y los datos iniciales de la administración al arrancar
builder.Services.AddHostedService<InicializadorPlanilla>();

// Strategy: hoy Google Sheets. Para sumar otra, se registra otra implementación acá.
builder.Services.AddSingleton<IFichajeStorageStrategy, GoogleSheetsStorageStrategy>();

// Observer: se pueden registrar varios observers; todos se inyectan como IEnumerable<IFichajeObserver>
builder.Services.AddSingleton<IFichajeObserver, LogFichajeObserver>();

builder.Services.AddScoped<FichajeService>();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Necesario para que los tests de integración (WebApplicationFactory) puedan levantar la API
public partial class Program { }
