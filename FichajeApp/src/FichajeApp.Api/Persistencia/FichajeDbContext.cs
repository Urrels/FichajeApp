using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FichajeApp.Api.Persistencia;

// Base de datos SQL del cliente (SQL Server / Azure SQL). Las tablas replican las pestañas de la planilla de Google:
// los repositorios de Persistencia/Sql implementan las mismas interfaces que los de Sheets y los servicios no cambian.
public class FichajeDbContext : DbContext
{
    public FichajeDbContext(DbContextOptions<FichajeDbContext> opciones) : base(opciones) { }

    public DbSet<EmpleadoFila> Empleados => Set<EmpleadoFila>();
    public DbSet<RolFila> Roles => Set<RolFila>();
    public DbSet<PermisoFila> Permisos => Set<PermisoFila>();
    public DbSet<RolPermisoFila> RolPermisos => Set<RolPermisoFila>();
    public DbSet<UsuarioRolFila> UsuarioRoles => Set<UsuarioRolFila>();
    public DbSet<BitacoraFila> Bitacora => Set<BitacoraFila>();
    public DbSet<HistorialFila> Historial => Set<HistorialFila>();
    public DbSet<FichajeFila> Fichajes => Set<FichajeFila>();
    public DbSet<SitioFila> Sitios => Set<SitioFila>();
    public DbSet<AparienciaFila> Apariencia => Set<AparienciaFila>();
    public DbSet<ConfigFichajeFila> ConfigFichaje => Set<ConfigFichajeFila>();
    public DbSet<ParametrosFila> Parametros => Set<ParametrosFila>();
    public DbSet<ValorHoraFila> ValoresHora => Set<ValorHoraFila>();
    public DbSet<HorarioFila> Horarios => Set<HorarioFila>();
    public DbSet<NotificacionFila> Notificaciones => Set<NotificacionFila>();
    public DbSet<RecorridoPuntoFila> RecorridoPuntos => Set<RecorridoPuntoFila>();
    public DbSet<FeriadoFila> Feriados => Set<FeriadoFila>();
    public DbSet<ExcedenteFila> Excedentes => Set<ExcedenteFila>();
    public DbSet<LiquidacionFila> Liquidaciones => Set<LiquidacionFila>();
    public DbSet<LiquidacionDiaFila> LiquidacionDias => Set<LiquidacionDiaFila>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<EmpleadoFila>(e =>
        {
            e.ToTable("Empleados");
            e.Property(x => x.NombreCompleto).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.PinHash).HasMaxLength(100).IsRequired();
            e.Property(x => x.Rol).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.NombreCompleto).IsUnique();
        });

        m.Entity<RolFila>(e =>
        {
            e.ToTable("Roles");
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        });
        m.Entity<PermisoFila>(e =>
        {
            e.ToTable("Permisos");
            e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Nombre).IsUnique();
        });
        m.Entity<RolPermisoFila>(e => { e.ToTable("RolPermiso"); e.HasKey(x => new { x.RolId, x.PermisoId }); });
        m.Entity<UsuarioRolFila>(e => { e.ToTable("UsuarioRol"); e.HasKey(x => new { x.EmpleadoId, x.RolId }); });

        m.Entity<BitacoraFila>(e =>
        {
            e.ToTable("Bitacora");
            e.Property(x => x.Usuario).HasMaxLength(200).IsRequired();
            e.Property(x => x.Accion).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.FechaHora);
        });
        m.Entity<HistorialFila>(e =>
        {
            e.ToTable("UsuarioHistorial");
            e.Property(x => x.Tipo).HasMaxLength(50).IsRequired();
            e.Property(x => x.RealizadoPor).HasMaxLength(200).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.RolIds).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.EmpleadoId);
        });

        m.Entity<FichajeFila>(e =>
        {
            e.ToTable("Fichajes");
            e.Property(x => x.NombreCompleto).HasMaxLength(200).IsRequired();
            e.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Lugar).HasMaxLength(200).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(500);
            e.Property(x => x.Direccion).HasMaxLength(500);
            e.Property(x => x.Foto).HasMaxLength(200);
            e.Property(x => x.MotivoCorreccion).HasMaxLength(700);
            e.Property(x => x.CorregidoPor).HasMaxLength(200);
            e.HasIndex(x => new { x.NombreCompleto, x.FechaHora });
            e.HasIndex(x => x.FechaHora);
        });

        m.Entity<SitioFila>(e =>
        {
            e.ToTable("Sitios");
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        });

        // Tablas de una sola fila (la configuración del cliente): el Id siempre es 1
        m.Entity<AparienciaFila>(e =>
        {
            e.ToTable("Apariencia");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.ColorPrimario).HasMaxLength(20);
            e.Property(x => x.ColorPrimarioTexto).HasMaxLength(20);
            e.Property(x => x.ColorAcento).HasMaxLength(20);
        });
        m.Entity<ConfigFichajeFila>(e =>
        {
            e.ToTable("ConfigFichaje");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Ubicacion).HasMaxLength(20).IsRequired();
            e.Property(x => x.Foto).HasMaxLength(20).IsRequired();
            e.Property(x => x.Modo).HasMaxLength(20).IsRequired();
        });
        m.Entity<ParametrosFila>(e =>
        {
            e.ToTable("ParametrosLiquidacion");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.JornadaNormalHoras).HasPrecision(9, 2);
            e.Property(x => x.RecargoExtraPct).HasPrecision(9, 2);
            e.Property(x => x.RecargoFeriadoPct).HasPrecision(9, 2);
        });

        m.Entity<ValorHoraFila>(e =>
        {
            e.ToTable("ValorHora");
            e.Property(x => x.Valor).HasPrecision(18, 4);
            e.Property(x => x.VigenteDesde).HasColumnType("date");
            e.Property(x => x.CargadoPor).HasMaxLength(200);
            e.HasIndex(x => new { x.EmpleadoId, x.VigenteDesde }).IsUnique();
        });
        m.Entity<RecorridoPuntoFila>(e =>
        {
            e.ToTable("RecorridoPuntos");
            e.HasIndex(x => new { x.EmpleadoId, x.FechaHora });
        });
        m.Entity<NotificacionFila>(e =>
        {
            e.ToTable("Notificaciones");
            e.Property(x => x.Titulo).HasMaxLength(120);
            e.Property(x => x.Mensaje).HasMaxLength(400);
            e.HasIndex(x => new { x.EmpleadoId, x.Fecha });
        });
        m.Entity<HorarioFila>(e =>
        {
            e.ToTable("Horarios");
            e.Property(x => x.Entrada).HasColumnType("time(0)");
            e.Property(x => x.Salida).HasColumnType("time(0)");
            e.HasIndex(x => new { x.EmpleadoId, x.DiaSemana }).IsUnique();
        });
        m.Entity<FeriadoFila>(e =>
        {
            e.ToTable("Feriados");
            e.HasKey(x => x.Fecha);
            e.Property(x => x.Fecha).HasColumnType("date").ValueGeneratedNever();
            e.Property(x => x.Descripcion).HasMaxLength(200).IsRequired();
            e.Property(x => x.Tipo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Origen).HasMaxLength(20).IsRequired();
        });
        m.Entity<ExcedenteFila>(e =>
        {
            e.ToTable("ExcedentesAprobados");
            e.Property(x => x.Empleado).HasMaxLength(200).IsRequired();
            e.Property(x => x.Fecha).HasColumnType("date");
            e.Property(x => x.AprobadoPor).HasMaxLength(200);
            e.HasIndex(x => new { x.Empleado, x.Fecha }).IsUnique();
        });
        m.Entity<LiquidacionFila>(e =>
        {
            e.ToTable("Liquidaciones");
            e.Property(x => x.Desde).HasColumnType("date");
            e.Property(x => x.Hasta).HasColumnType("date");
            e.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            e.Property(x => x.CerradaPor).HasMaxLength(200).IsRequired();
            e.Property(x => x.PagadaPor).HasMaxLength(200);
            e.HasMany(x => x.Dias).WithOne().HasForeignKey(d => d.LiquidacionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<LiquidacionDiaFila>(e =>
        {
            e.ToTable("LiquidacionDias");
            e.Property(x => x.Empleado).HasMaxLength(200).IsRequired();
            e.Property(x => x.Fecha).HasColumnType("date");
            e.Property(x => x.ValorHora).HasPrecision(18, 4);
            e.Property(x => x.RecargoExtraPct).HasPrecision(9, 2);
            e.Property(x => x.RecargoFeriadoPct).HasPrecision(9, 2);
            e.Property(x => x.Importe).HasPrecision(18, 2);
            e.Property(x => x.Observacion).HasMaxLength(700);
            e.HasIndex(x => x.LiquidacionId);
        });
    }
}

// Lo usa la herramienta de migraciones (dotnet ef): no se conecta a nada, solo necesita saber qué motor es
public class FichajeDbContextFactoryDeDiseno : IDesignTimeDbContextFactory<FichajeDbContext>
{
    public FichajeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FichajeDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Fichaje;Trusted_Connection=True;").Options);
}

// ---- Filas (una clase por tabla) ----

public class EmpleadoFila
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Email { get; set; } = "";
    public string PinHash { get; set; } = "";
    public bool Activo { get; set; }
    public string Rol { get; set; } = "Empleado";
    public int IntentosFallidos { get; set; }
    public bool Bloqueado { get; set; }
}

public class RolFila { public int Id { get; set; } public string Nombre { get; set; } = ""; public int? PadreId { get; set; } public bool Protegido { get; set; } }
public class PermisoFila { public int Id { get; set; } public string Nombre { get; set; } = ""; }
public class RolPermisoFila { public int RolId { get; set; } public int PermisoId { get; set; } }
public class UsuarioRolFila { public int EmpleadoId { get; set; } public int RolId { get; set; } }

public class BitacoraFila
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = "";
    public string Accion { get; set; } = "";
}

public class HistorialFila
{
    public int Id { get; set; }
    public DateTime FechaHora { get; set; }
    public int EmpleadoId { get; set; }
    public string Tipo { get; set; } = "";
    public string RealizadoPor { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public bool Activo { get; set; }
    public bool Bloqueado { get; set; }
    public int IntentosFallidos { get; set; }
    public string RolIds { get; set; } = "";   // "1,2,3"
    public int? VersionOrigen { get; set; }
}

public class FichajeFila
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = "";
    public DateTime FechaHora { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public string Tipo { get; set; } = "";
    public string Lugar { get; set; } = "";
    public string? Motivo { get; set; }
    public double? Precision { get; set; }
    public string? Direccion { get; set; }
    public string? Foto { get; set; }
    // Cargado a mano por un administrador: figura en las liquidaciones pero no cuenta para "qué toca fichar ahora"
    public bool EsManual { get; set; }
    public bool Corregido { get; set; }
    public string? MotivoCorreccion { get; set; }
    public string? CorregidoPor { get; set; }
    public DateTime? FechaCorreccion { get; set; }
}

public class SitioFila
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double RadioMetros { get; set; }
}

public class AparienciaFila
{
    public int Id { get; set; } = 1;
    public string? Nombre { get; set; }
    public string? ColorPrimario { get; set; }
    public string? ColorPrimarioTexto { get; set; }
    public string? ColorAcento { get; set; }
    public byte[]? Logo { get; set; }
}

public class ConfigFichajeFila
{
    public int Id { get; set; } = 1;
    public string Ubicacion { get; set; } = "Libre";
    public string Foto { get; set; } = "No";
    public string Modo { get; set; } = "Personal";
    public int PrecisionMaxMetros { get; set; }
    public bool SeguimientoRecorrido { get; set; }
}

public class RecorridoPuntoFila
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public DateTime FechaHora { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double? Precision { get; set; }
}

public class ParametrosFila
{
    public int Id { get; set; } = 1;
    public int ToleranciaMinutos { get; set; } = 10;
    public int RedondeoMinutos { get; set; } = 15;
    public decimal JornadaNormalHoras { get; set; } = 8;
    public decimal RecargoExtraPct { get; set; } = 50;
    public decimal RecargoFeriadoPct { get; set; } = 100;
    public int MaxHorasFichajeAbierto { get; set; } = 16;
    public bool PagarPuentes { get; set; }
}

public class ValorHoraFila
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public decimal Valor { get; set; }
    public DateTime VigenteDesde { get; set; }
    public string? CargadoPor { get; set; }
}

public class NotificacionFila
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public DateTime Fecha { get; set; }
    public bool Leida { get; set; }
}

public class HorarioFila
{
    public int Id { get; set; }
    public int EmpleadoId { get; set; }
    public int DiaSemana { get; set; }   // 0 = domingo ... 6 = sábado (DayOfWeek)
    public TimeSpan Entrada { get; set; }
    public TimeSpan Salida { get; set; }
}

public class FeriadoFila
{
    public DateTime Fecha { get; set; }
    public string Descripcion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Origen { get; set; } = "manual";
}

public class ExcedenteFila
{
    public int Id { get; set; }
    public string Empleado { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? AprobadoPor { get; set; }
}

public class LiquidacionFila
{
    public int Id { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public string Estado { get; set; } = "Cerrada";
    public string CerradaPor { get; set; } = "";
    public DateTime FechaCierre { get; set; }
    public string? PagadaPor { get; set; }
    public DateTime? FechaPago { get; set; }
    public List<LiquidacionDiaFila> Dias { get; set; } = new();
}

public class LiquidacionDiaFila
{
    public int Id { get; set; }
    public int LiquidacionId { get; set; }
    public int EmpleadoId { get; set; }
    public string Empleado { get; set; } = "";
    public DateTime Fecha { get; set; }
    public DateTime? Ingreso { get; set; }
    public DateTime? Egreso { get; set; }
    public int MinTrabajados { get; set; }
    public int MinAsignados { get; set; }
    public int MinPagables { get; set; }
    public int MinNormales { get; set; }
    public int MinExtra { get; set; }
    public int MinFeriado { get; set; }
    public int MinExcedente { get; set; }
    public bool ExcedenteAprobado { get; set; }
    public bool Ausente { get; set; }
    public decimal ValorHora { get; set; }
    public decimal RecargoExtraPct { get; set; }
    public decimal RecargoFeriadoPct { get; set; }
    public decimal Importe { get; set; }
    public string? Observacion { get; set; }
}
