using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117170000_AddSalonRegistry")]
public sealed class AddSalonRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE saas_saloes (
            id uuid PRIMARY KEY,
            versao bigint NOT NULL DEFAULT 1,
            nome varchar(120) NOT NULL,
            identificador varchar(50) NOT NULL UNIQUE,
            responsavel varchar(120) NOT NULL DEFAULT '',
            email varchar(254),
            whatsapp varchar(15),
            url_publica varchar(300),
            megahairpro boolean NOT NULL DEFAULT false,
            atualizado timestamptz NOT NULL DEFAULT now(),
            CHECK (identificador ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
        );
        CREATE UNIQUE INDEX saas_saloes_url ON saas_saloes (url_publica) WHERE url_publica IS NOT NULL;
        CREATE UNIQUE INDEX saas_saloes_tati ON saas_saloes (megahairpro) WHERE megahairpro;
        INSERT INTO saas_saloes (id, nome, identificador, url_publica, megahairpro)
        VALUES ('11111111-1111-4111-8111-111111111111', 'Tati Mega Hair', 'tati',
                'https://simulador.tatimegahair.com.br', true);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE saas_saloes;");
}
