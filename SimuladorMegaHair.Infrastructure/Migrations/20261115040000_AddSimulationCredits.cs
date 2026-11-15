using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261115040000_AddSimulationCredits")]
public sealed class AddSimulationCredits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE simulador_creditos_carteira (
            chave text PRIMARY KEY,
            gratuito_usado boolean NOT NULL DEFAULT false,
            saldo integer NOT NULL DEFAULT 0 CHECK (saldo >= 0)
        );
        CREATE TABLE simulador_creditos_dia (
            dia date PRIMARY KEY,
            usadas integer NOT NULL CHECK (usadas >= 0)
        );
        CREATE TABLE simulador_creditos_pix (
            id uuid PRIMARY KEY,
            carteira text NOT NULL,
            valor numeric(10,2) NOT NULL CHECK (valor > 0),
            creditos integer NOT NULL CHECK (creditos > 0),
            pagamento bigint UNIQUE,
            liberado boolean NOT NULL DEFAULT false,
            criado timestamptz NOT NULL DEFAULT now()
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE simulador_creditos_pix;
        DROP TABLE simulador_creditos_dia;
        DROP TABLE simulador_creditos_carteira;
        """);
}
