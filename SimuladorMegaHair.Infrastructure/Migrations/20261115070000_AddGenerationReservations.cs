using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261115070000_AddGenerationReservations")]
public sealed class AddGenerationReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE simulador_creditos_reserva (
            id uuid PRIMARY KEY,
            carteira text NOT NULL REFERENCES simulador_creditos_carteira(chave),
            gratuita boolean NOT NULL,
            expira timestamptz NOT NULL DEFAULT (now() + interval '20 minutes')
        );
        CREATE INDEX simulador_creditos_reserva_expira ON simulador_creditos_reserva (expira);
        ALTER TABLE simulador_creditos_pix ADD COLUMN reconciliar_em timestamptz NOT NULL DEFAULT now();
        CREATE INDEX simulador_creditos_pix_reconciliar ON simulador_creditos_pix (reconciliar_em)
            WHERE NOT liberado AND pagamento IS NOT NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX simulador_creditos_pix_reconciliar;
            ALTER TABLE simulador_creditos_pix DROP COLUMN reconciliar_em;
            DROP TABLE simulador_creditos_reserva;
            """);
}
