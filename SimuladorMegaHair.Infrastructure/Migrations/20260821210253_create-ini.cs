using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimuladorMegaHair.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class createini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClienteGostou",
                table: "Simulacoes");

            migrationBuilder.AlterColumn<decimal>(
                name: "ValorEstimado",
                table: "Simulacoes",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FotoResultadoPath",
                table: "Simulacoes",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderUtilizado",
                table: "Simulacoes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TempoProcessamentoMs",
                table: "Simulacoes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderUtilizado",
                table: "Simulacoes");

            migrationBuilder.DropColumn(
                name: "TempoProcessamentoMs",
                table: "Simulacoes");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clientes");

            migrationBuilder.AlterColumn<decimal>(
                name: "ValorEstimado",
                table: "Simulacoes",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<string>(
                name: "FotoResultadoPath",
                table: "Simulacoes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "ClienteGostou",
                table: "Simulacoes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
