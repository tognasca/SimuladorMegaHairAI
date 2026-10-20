using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimuladorMegaHair.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndSimulationCacheIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Simulacoes_FotoOriginalPath_Comprimento_Cor_TipoCabelo_Meto~",
                table: "Simulacoes",
                columns: new[] { "FotoOriginalPath", "Comprimento", "Cor", "TipoCabelo", "MetodoMegaHair", "ProviderUtilizado" });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Simulacoes_FotoOriginalPath_Comprimento_Cor_TipoCabelo_Meto~",
                table: "Simulacoes");
        }
    }
}
