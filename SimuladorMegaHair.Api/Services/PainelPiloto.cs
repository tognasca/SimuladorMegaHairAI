using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Services;

public sealed class PainelPiloto(AppDbContext db, IConfiguration config)
{
    private Configuracao? _configuracao;

    public sealed record Configuracao(bool PublicoAtivo, int LimiteDiario, decimal Preco, int Quantidade, decimal CustoIa);
    public sealed record Indicadores(int GratuitasHoje, long PacotesLiberados, decimal ReceitaPix, long CreditosDisponiveis,
        long GeracoesPublicasHoje, long GeracoesEquipeHoje, decimal CustoPublicoHoje, decimal CustoEquipeHoje, long CliquesHoje);

    public async Task<Configuracao> LerAsync(CancellationToken ct)
    {
        if (_configuracao is not null) return _configuracao;

        var rows = await db.Database.SqlQueryRaw<Configuracao>("""
            SELECT publico_ativo AS "PublicoAtivo", limite_diario AS "LimiteDiario", preco AS "Preco",
                   quantidade AS "Quantidade", custo_ia AS "CustoIa"
            FROM simulador_piloto_config WHERE id = 1
            """).ToListAsync(ct);

        return _configuracao = rows.SingleOrDefault() ?? new(
            config.GetValue("Creditos:PublicoAtivo", true),
            Math.Clamp(config.GetValue("Creditos:LimiteDiarioGratis", 10), 0, 10000),
            decimal.Round(Math.Clamp(config.GetValue("Creditos:Preco", 10m), 1m, 1000m), 2),
            Math.Clamp(config.GetValue("Creditos:Quantidade", 2), 1, 20),
            Math.Clamp(config.GetValue("Creditos:CustoIa", 0m), 0m, 1000m));
    }

    public async Task SalvarAsync(Configuracao valores, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_piloto_config (id, publico_ativo, limite_diario, preco, quantidade, custo_ia)
            VALUES (1, {valores.PublicoAtivo}, {valores.LimiteDiario}, {valores.Preco}, {valores.Quantidade}, {valores.CustoIa})
            ON CONFLICT (id) DO UPDATE SET publico_ativo = EXCLUDED.publico_ativo,
                limite_diario = EXCLUDED.limite_diario, preco = EXCLUDED.preco,
                quantidade = EXCLUDED.quantidade, custo_ia = EXCLUDED.custo_ia
            """, ct);
        _configuracao = null;
    }

    public Task RegistrarAsync(string tipo, Guid id, decimal custo, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_piloto_evento (tipo, chave, custo)
            VALUES ({tipo}, {id}, {custo}) ON CONFLICT DO NOTHING
            """, ct);

    public async Task<Indicadores> IndicadoresAsync(CancellationToken ct)
    {
        var resultado = await db.Database.SqlQueryRaw<Indicadores>("""
            WITH hoje AS (
                SELECT (now() AT TIME ZONE 'America/Sao_Paulo')::date AS dia
            ), eventos AS (
                SELECT * FROM simulador_piloto_evento, hoje
                WHERE criado >= (dia::timestamp AT TIME ZONE 'America/Sao_Paulo')
                  AND criado < ((dia + 1)::timestamp AT TIME ZONE 'America/Sao_Paulo')
            )
            SELECT COALESCE((SELECT usadas FROM simulador_creditos_dia JOIN hoje USING (dia)), 0) AS "GratuitasHoje",
                   (SELECT count(*) FROM simulador_creditos_pix WHERE liberado) AS "PacotesLiberados",
                   COALESCE((SELECT sum(valor) FROM simulador_creditos_pix WHERE liberado), 0) AS "ReceitaPix",
                   COALESCE((SELECT sum(saldo) FROM simulador_creditos_carteira), 0) AS "CreditosDisponiveis",
                   count(*) FILTER (WHERE tipo = 'publico') AS "GeracoesPublicasHoje",
                   count(*) FILTER (WHERE tipo = 'equipe') AS "GeracoesEquipeHoje",
                   COALESCE(sum(custo) FILTER (WHERE tipo = 'publico'), 0) AS "CustoPublicoHoje",
                   COALESCE(sum(custo) FILTER (WHERE tipo = 'equipe'), 0) AS "CustoEquipeHoje",
                   count(*) FILTER (WHERE tipo = 'whatsapp') AS "CliquesHoje"
            FROM eventos
            """).ToListAsync(ct);
        return resultado.Single();
    }
}
