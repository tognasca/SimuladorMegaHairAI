using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261115060000_AddLocalQuote")]
public sealed class AddLocalQuote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE simulador_orcamento (
            id integer PRIMARY KEY CHECK (id = 1),
            tabela jsonb NOT NULL,
            atualizado timestamptz NOT NULL DEFAULT now()
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE simulador_orcamento;");
}
