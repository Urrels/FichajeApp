using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FichajeApp.Api.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Horarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Horarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    DiaSemana = table.Column<int>(type: "int", nullable: false),
                    Entrada = table.Column<TimeSpan>(type: "time(0)", nullable: false),
                    Salida = table.Column<TimeSpan>(type: "time(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Horarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Horarios_EmpleadoId_DiaSemana",
                table: "Horarios",
                columns: new[] { "EmpleadoId", "DiaSemana" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Horarios");
        }
    }
}
