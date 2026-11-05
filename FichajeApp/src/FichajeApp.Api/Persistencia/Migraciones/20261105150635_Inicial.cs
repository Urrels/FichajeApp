using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FichajeApp.Api.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apariencia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ColorPrimario = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ColorPrimarioTexto = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ColorAcento = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Logo = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apariencia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bitacora", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigFichaje",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Foto = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Modo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrecisionMaxMetros = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigFichaje", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Empleados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreCompleto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    Bloqueado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empleados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExcedentesAprobados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Empleado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fecha = table.Column<DateTime>(type: "date", nullable: false),
                    AprobadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcedentesAprobados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Feriados",
                columns: table => new
                {
                    Fecha = table.Column<DateTime>(type: "date", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feriados", x => x.Fecha);
                });

            migrationBuilder.CreateTable(
                name: "Fichajes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreCompleto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Latitud = table.Column<double>(type: "float", nullable: false),
                    Longitud = table.Column<double>(type: "float", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Lugar = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Precision = table.Column<double>(type: "float", nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Foto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EsManual = table.Column<bool>(type: "bit", nullable: false),
                    Corregido = table.Column<bool>(type: "bit", nullable: false),
                    MotivoCorreccion = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                    CorregidoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaCorreccion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fichajes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Liquidaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Desde = table.Column<DateTime>(type: "date", nullable: false),
                    Hasta = table.Column<DateTime>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CerradaPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PagadaPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Liquidaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParametrosLiquidacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ToleranciaMinutos = table.Column<int>(type: "int", nullable: false),
                    RedondeoMinutos = table.Column<int>(type: "int", nullable: false),
                    JornadaNormalHoras = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    RecargoExtraPct = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    RecargoFeriadoPct = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MaxHorasFichajeAbierto = table.Column<int>(type: "int", nullable: false),
                    PagarPuentes = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParametrosLiquidacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permisos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permisos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PadreId = table.Column<int>(type: "int", nullable: true),
                    Protegido = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolPermiso",
                columns: table => new
                {
                    RolId = table.Column<int>(type: "int", nullable: false),
                    PermisoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolPermiso", x => new { x.RolId, x.PermisoId });
                });

            migrationBuilder.CreateTable(
                name: "Sitios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Latitud = table.Column<double>(type: "float", nullable: false),
                    Longitud = table.Column<double>(type: "float", nullable: false),
                    RadioMetros = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sitios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioHistorial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RealizadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    Bloqueado = table.Column<bool>(type: "bit", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    RolIds = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    VersionOrigen = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioHistorial", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioRol",
                columns: table => new
                {
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    RolId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioRol", x => new { x.EmpleadoId, x.RolId });
                });

            migrationBuilder.CreateTable(
                name: "ValorHora",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VigenteDesde = table.Column<DateTime>(type: "date", nullable: false),
                    CargadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValorHora", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiquidacionDias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LiquidacionId = table.Column<int>(type: "int", nullable: false),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    Empleado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fecha = table.Column<DateTime>(type: "date", nullable: false),
                    Ingreso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Egreso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MinTrabajados = table.Column<int>(type: "int", nullable: false),
                    MinAsignados = table.Column<int>(type: "int", nullable: false),
                    MinPagables = table.Column<int>(type: "int", nullable: false),
                    MinNormales = table.Column<int>(type: "int", nullable: false),
                    MinExtra = table.Column<int>(type: "int", nullable: false),
                    MinFeriado = table.Column<int>(type: "int", nullable: false),
                    MinExcedente = table.Column<int>(type: "int", nullable: false),
                    ExcedenteAprobado = table.Column<bool>(type: "bit", nullable: false),
                    Ausente = table.Column<bool>(type: "bit", nullable: false),
                    ValorHora = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RecargoExtraPct = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    RecargoFeriadoPct = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observacion = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidacionDias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiquidacionDias_Liquidaciones_LiquidacionId",
                        column: x => x.LiquidacionId,
                        principalTable: "Liquidaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_FechaHora",
                table: "Bitacora",
                column: "FechaHora");

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_Email",
                table: "Empleados",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_NombreCompleto",
                table: "Empleados",
                column: "NombreCompleto",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExcedentesAprobados_Empleado_Fecha",
                table: "ExcedentesAprobados",
                columns: new[] { "Empleado", "Fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fichajes_FechaHora",
                table: "Fichajes",
                column: "FechaHora");

            migrationBuilder.CreateIndex(
                name: "IX_Fichajes_NombreCompleto_FechaHora",
                table: "Fichajes",
                columns: new[] { "NombreCompleto", "FechaHora" });

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacionDias_LiquidacionId",
                table: "LiquidacionDias",
                column: "LiquidacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Permisos_Nombre",
                table: "Permisos",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuarioHistorial_EmpleadoId",
                table: "UsuarioHistorial",
                column: "EmpleadoId");

            migrationBuilder.CreateIndex(
                name: "IX_ValorHora_EmpleadoId_VigenteDesde",
                table: "ValorHora",
                columns: new[] { "EmpleadoId", "VigenteDesde" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Apariencia");

            migrationBuilder.DropTable(
                name: "Bitacora");

            migrationBuilder.DropTable(
                name: "ConfigFichaje");

            migrationBuilder.DropTable(
                name: "Empleados");

            migrationBuilder.DropTable(
                name: "ExcedentesAprobados");

            migrationBuilder.DropTable(
                name: "Feriados");

            migrationBuilder.DropTable(
                name: "Fichajes");

            migrationBuilder.DropTable(
                name: "LiquidacionDias");

            migrationBuilder.DropTable(
                name: "ParametrosLiquidacion");

            migrationBuilder.DropTable(
                name: "Permisos");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "RolPermiso");

            migrationBuilder.DropTable(
                name: "Sitios");

            migrationBuilder.DropTable(
                name: "UsuarioHistorial");

            migrationBuilder.DropTable(
                name: "UsuarioRol");

            migrationBuilder.DropTable(
                name: "ValorHora");

            migrationBuilder.DropTable(
                name: "Liquidaciones");
        }
    }
}
