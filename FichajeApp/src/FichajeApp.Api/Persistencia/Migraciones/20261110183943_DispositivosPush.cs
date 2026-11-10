using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FichajeApp.Api.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DispositivosPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispositivosPush",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Actualizado = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispositivosPush", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispositivosPush_EmpleadoId",
                table: "DispositivosPush",
                column: "EmpleadoId");

            migrationBuilder.CreateIndex(
                name: "IX_DispositivosPush_Token",
                table: "DispositivosPush",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispositivosPush");
        }
    }
}
