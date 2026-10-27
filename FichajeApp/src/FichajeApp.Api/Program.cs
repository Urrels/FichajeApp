using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FichajeApp.Api.Concurrency;
using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Options;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Servicios de datos (Google Sheets)
builder.Services.Configure<FichajeOptions>(builder.Configuration.GetSection(FichajeOptions.Seccion));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<GoogleSheetsClient>();
builder.Services.AddSingleton<BloqueoPorClave>();

// Se registra también la clase concreta porque AuthController (login) la inyecta directamente.
builder.Services.AddSingleton<GoogleSheetsEmpleadosService>();
// Decorator: las consultas del fichaje pasan por una caché sobre el servicio de Sheets.
builder.Services.AddSingleton<IEmpleadosService>(sp => new CachedEmpleadosService(
    sp.GetRequiredService<GoogleSheetsEmpleadosService>(),
    sp.GetRequiredService<IMemoryCache>(),
    sp.GetRequiredService<IOptions<FichajeOptions>>()));

builder.Services.AddSingleton<GoogleSheetsSitiosService>();
builder.Services.AddSingleton<ISitiosRepository>(sp => new CachedSitiosRepository(
    sp.GetRequiredService<GoogleSheetsSitiosService>(),
    sp.GetRequiredService<IMemoryCache>(),
    sp.GetRequiredService<IOptions<FichajeOptions>>()));
builder.Services.AddSingleton<ISitiosService, SitiosService>();
builder.Services.AddSingleton<JwtService>();

// Strategy: hoy Google Sheets. Para sumar otra, se registra otra implementación acá.
builder.Services.AddSingleton<GoogleSheetsStorageStrategy>();
builder.Services.AddSingleton<IFichajeStorageStrategy>(sp => new CachedFichajeStorage(
    sp.GetRequiredService<GoogleSheetsStorageStrategy>(),
    sp.GetRequiredService<IMemoryCache>()));

// Observer: se pueden registrar varios observers; todos se inyectan como IEnumerable<IFichajeObserver>
builder.Services.AddSingleton<IFichajeObserver, LogFichajeObserver>();

builder.Services.AddScoped<IFichajeService, FichajeService>();

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
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
