using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Services;

public sealed class CreditosSimulacao(AppDbContext db, IConfiguration config, PainelPiloto painel)
{
    public bool Ativo => config.GetValue<bool>("Creditos:Ativo");
    public static string? Carteira(string token)
    {
        if (token.Length != 64 || !token.All(char.IsAsciiHexDigit)) return null;
        return Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token)));
    }

    public enum Reserva { Bloqueada, Gratis, Paga }

    public async Task<Reserva> ReservarAsync(string carteira, Guid id, CancellationToken ct)
    {
        await ReconciliarExpiradasAsync(ct);
        var ajustes = await painel.LerAsync(ct);
        if (!ajustes.PublicoAtivo) return Reserva.Bloqueada;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_creditos_carteira (chave) VALUES ({carteira}) ON CONFLICT DO NOTHING
            """, ct);

        var gratuita = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_carteira SET gratuito_usado = true
            WHERE chave = {carteira} AND gratuito_usado = false
            """, ct);
        if (gratuita == 1 && ajustes.LimiteDiario > 0)
        {
            var limitada = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO simulador_creditos_dia (dia, usadas)
                VALUES ((now() AT TIME ZONE 'America/Sao_Paulo')::date, 1)
                ON CONFLICT (dia) DO UPDATE SET usadas = simulador_creditos_dia.usadas + 1
                WHERE simulador_creditos_dia.usadas < {ajustes.LimiteDiario}
                """, ct);
            if (limitada == 1)
            {
                await RegistrarReservaAsync(carteira, id, true, ct);
                await tx.CommitAsync(ct);
                return Reserva.Gratis;
            }
        }

        if (gratuita == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE simulador_creditos_carteira SET gratuito_usado = false WHERE chave = {carteira}
                """, ct);

        var paga = Ativo ? await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_carteira SET saldo = saldo - 1
            WHERE chave = {carteira} AND saldo > 0
            """, ct) : 0;
        if (paga == 1) await RegistrarReservaAsync(carteira, id, false, ct);
        await tx.CommitAsync(ct);
        return paga == 1 ? Reserva.Paga : Reserva.Bloqueada;
    }

    private Task RegistrarReservaAsync(string carteira, Guid id, bool gratuita, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_creditos_reserva (id, carteira, gratuita)
            VALUES ({id}, {carteira}, {gratuita})
            """, ct);

    public async Task ConfirmarAsync(Guid id, CancellationToken ct)
    {
        var removida = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM simulador_creditos_reserva WHERE id = {id} AND expira > now()
            """, ct);
        if (removida != 1)
            throw new InvalidOperationException("A reserva de geração expirou.");
    }

    public Task EstornarAsync(Guid id) => ReconciliarReservasAsync(id, CancellationToken.None);

    public Task ReconciliarExpiradasAsync(CancellationToken ct) => ReconciliarReservasAsync(null, ct);

    private Task ReconciliarReservasAsync(Guid? id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH removidas AS (
                DELETE FROM simulador_creditos_reserva
                WHERE id = {id.GetValueOrDefault()} OR expira <= now()
                RETURNING carteira, gratuita
            ), total AS (
                SELECT carteira, bool_or(gratuita) AS gratuita,
                       CAST(count(*) FILTER (WHERE NOT gratuita) AS integer) AS pagas
                FROM removidas GROUP BY carteira
            )
            UPDATE simulador_creditos_carteira AS c
            SET gratuito_usado = c.gratuito_usado AND NOT t.gratuita,
                saldo = c.saldo + t.pagas
            FROM total AS t WHERE c.chave = t.carteira
            """, ct);

    public async Task<CarteiraRecuperada?> RecuperarAsync(string carteira, CancellationToken ct)
    {
        await ReconciliarExpiradasAsync(ct);
        var rows = await db.Database.SqlQuery<CarteiraRecuperada>($"""
            SELECT COALESCE((SELECT saldo FROM simulador_creditos_carteira WHERE chave = {carteira}), 0) AS "Saldo",
                   (SELECT id FROM simulador_creditos_pix WHERE carteira = {carteira}
                    ORDER BY criado DESC, id DESC LIMIT 1) AS "Pedido"
            WHERE EXISTS (SELECT 1 FROM simulador_creditos_carteira WHERE chave = {carteira})
               OR EXISTS (SELECT 1 FROM simulador_creditos_pix WHERE carteira = {carteira})
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public sealed record CarteiraRecuperada(int Saldo, Guid? Pedido);

    public async Task<PedidoPix?> PedidoAsync(Guid id, string carteira, bool criar, CancellationToken ct)
    {
        if (criar)
        {
            var ajustes = await painel.LerAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO simulador_creditos_pix (id, carteira, valor, creditos)
                VALUES ({id}, {carteira}, {ajustes.Preco}, {ajustes.Quantidade}) ON CONFLICT DO NOTHING
                """, ct);
        }

        var pedidos = await db.Database.SqlQuery<PedidoPix>($"""
            SELECT id AS "Id", valor AS "Valor", creditos AS "Creditos", pagamento AS "Pagamento",
                   liberado AS "Liberado"
            FROM simulador_creditos_pix WHERE id = {id} AND carteira = {carteira}
            """).ToListAsync(ct);
        return pedidos.SingleOrDefault();
    }

    public Task<List<PedidoPix>> PedidosParaReconciliarAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<PedidoPix>("""
            WITH lote AS (
                SELECT id FROM simulador_creditos_pix
                WHERE NOT liberado AND pagamento IS NOT NULL AND reconciliar_em <= now()
                ORDER BY reconciliar_em
                LIMIT 10 FOR UPDATE SKIP LOCKED
            )
            UPDATE simulador_creditos_pix AS p
            SET reconciliar_em = now() + interval '5 minutes'
            FROM lote WHERE p.id = lote.id
            RETURNING p.id AS "Id", p.valor AS "Valor", p.creditos AS "Creditos",
                      p.pagamento AS "Pagamento", p.liberado AS "Liberado"
            """).ToListAsync(ct);

    public async Task EncerrarReconciliacaoAsync(Guid pedido, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_pix SET reconciliar_em = 'infinity'::timestamptz WHERE id = {pedido}
            """, ct);

    public async Task RegistrarPagamentoAsync(Guid pedido, long pagamento, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_pix SET pagamento = {pagamento}
            WHERE id = {pedido} AND pagamento IS NULL
            """, ct);

    public async Task LiberarAsync(Guid pedido, long pagamento, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH liberacao AS (
                UPDATE simulador_creditos_pix SET liberado = true
                WHERE id = {pedido} AND pagamento = {pagamento} AND liberado = false
                RETURNING carteira, creditos
            )
            INSERT INTO simulador_creditos_carteira (chave, saldo)
            SELECT carteira, creditos FROM liberacao
            ON CONFLICT (chave) DO UPDATE SET saldo = simulador_creditos_carteira.saldo + EXCLUDED.saldo
            """, ct);
    }

    public sealed record PedidoPix(Guid Id, decimal Valor, int Creditos, long? Pagamento, bool Liberado);
}
