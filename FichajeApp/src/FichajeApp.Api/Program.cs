using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using FichajeApp.Api.Observers;
using FichajeApp.Api.Services;
using FichajeApp.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Servicios de datos (Google Sheets)
builder.Services.AddSingleton<GoogleSheetsEmpleadosService>();
builder.Services.AddSingleton<GoogleSheetsSitiosService>();
builder.Services.AddSingleton<JwtService>();

// Strategy: hoy Google Sheets. Para sumar otra, se registra otra implementación acá.
builder.Services.AddSingleton<IFichajeStorageStrategy, GoogleSheetsStorageStrategy>();

// Observer: se pueden registrar varios observers; todos se inyectan como IEnumerable<IFichajeObserver>
builder.Services.AddSingleton<IFichajeObserver, LogFichajeObserver>();

builder.Services.AddScoped<FichajeService>();

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
