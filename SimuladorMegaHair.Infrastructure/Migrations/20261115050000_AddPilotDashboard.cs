using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261115050000_AddPilotDashboard")]
public sealed class AddPilotDashboard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE simulador_piloto_config (
            id integer PRIMARY KEY CHECK (id = 1),
            publico_ativo boolean NOT NULL,
            limite_diario integer NOT NULL,
            preco numeric(10,2) NOT NULL,
            quantidade integer NOT NULL,
            custo_ia numeric(10,4) NOT NULL
        );
        CREATE TABLE simulador_piloto_evento (
            tipo text NOT NULL CHECK (tipo IN ('publico','equipe','whatsapp')),
            chave uuid NOT NULL,
            custo numeric(10,4) NOT NULL DEFAULT 0,
            criado timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (tipo, chave)
        );
        CREATE INDEX simulador_piloto_evento_criado ON simulador_piloto_evento (criado);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE simulador_piloto_evento;
        DROP TABLE simulador_piloto_config;
        """);
}
