using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FichajeApp.Api.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Recorrido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SeguimientoRecorrido",
                table: "ConfigFichaje",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RecorridoPuntos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Latitud = table.Column<double>(type: "float", nullable: false),
                    Longitud = table.Column<double>(type: "float", nullable: false),
                    Precision = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecorridoPuntos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecorridoPuntos_EmpleadoId_FechaHora",
                table: "RecorridoPuntos",
                columns: new[] { "EmpleadoId", "FechaHora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecorridoPuntos");

            migrationBuilder.DropColumn(
                name: "SeguimientoRecorrido",
                table: "ConfigFichaje");
        }
    }
}
