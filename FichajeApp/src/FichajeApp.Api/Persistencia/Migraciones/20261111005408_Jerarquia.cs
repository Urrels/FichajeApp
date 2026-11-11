using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FichajeApp.Api.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Jerarquia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupervisorId",
                table: "Empleados",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_SupervisorId",
                table: "Empleados",
                column: "SupervisorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Empleados_Empleados_SupervisorId",
                table: "Empleados",
                column: "SupervisorId",
                principalTable: "Empleados",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Empleados_Empleados_SupervisorId",
                table: "Empleados");

            migrationBuilder.DropIndex(
                name: "IX_Empleados_SupervisorId",
                table: "Empleados");

            migrationBuilder.DropColumn(
                name: "SupervisorId",
                table: "Empleados");
        }
    }
}
