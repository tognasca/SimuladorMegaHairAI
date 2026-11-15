using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Services;

public sealed class CreditosSimulacao(AppDbContext db, IConfiguration config)
{
    public bool Ativo => config.GetValue<bool>("Creditos:Ativo");
    public bool PublicoAtivo => config.GetValue("Creditos:PublicoAtivo", true);
    public decimal Preco => decimal.Round(Math.Clamp(config.GetValue("Creditos:Preco", 10m), 1m, 1000m), 2);
    public int Quantidade => Math.Clamp(config.GetValue("Creditos:Quantidade", 2), 1, 20);
    private int LimiteDiario => Math.Clamp(config.GetValue("Creditos:LimiteDiarioGratis", 10), 0, 10000);

    public static string? Carteira(string token)
    {
        if (token.Length != 64 || !token.All(char.IsAsciiHexDigit)) return null;
        return Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token)));
    }

    public enum Reserva { Bloqueada, Gratis, Paga }

    public async Task<Reserva> ReservarAsync(string carteira, CancellationToken ct)
    {
        if (!PublicoAtivo) return Reserva.Bloqueada;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_creditos_carteira (chave) VALUES ({carteira}) ON CONFLICT DO NOTHING
            """, ct);

        var gratuita = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_carteira SET gratuito_usado = true
            WHERE chave = {carteira} AND gratuito_usado = false
            """, ct);
        if (gratuita == 1 && LimiteDiario > 0)
        {
            var limitada = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO simulador_creditos_dia (dia, usadas)
                VALUES ((now() AT TIME ZONE 'America/Sao_Paulo')::date, 1)
                ON CONFLICT (dia) DO UPDATE SET usadas = simulador_creditos_dia.usadas + 1
                WHERE simulador_creditos_dia.usadas < {LimiteDiario}
                """, ct);
            if (limitada == 1)
            {
                await tx.CommitAsync(ct);
                return Reserva.Gratis;
            }
        }

        if (gratuita == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE simulador_creditos_carteira SET gratuito_usado = false WHERE chave = {carteira}
                """, ct);

        var paga = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE simulador_creditos_carteira SET saldo = saldo - 1
            WHERE chave = {carteira} AND saldo > 0
            """, ct);
        await tx.CommitAsync(ct);
        return paga == 1 ? Reserva.Paga : Reserva.Bloqueada;
    }

    public Task EstornarAsync(string carteira, Reserva reserva) =>
        reserva == Reserva.Gratis
            ? db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE simulador_creditos_carteira SET gratuito_usado = false WHERE chave = {carteira}
                """)
            : db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE simulador_creditos_carteira SET saldo = saldo + 1 WHERE chave = {carteira}
                """);

    public async Task<PedidoPix?> PedidoAsync(Guid id, string carteira, bool criar, CancellationToken ct)
    {
        if (criar)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO simulador_creditos_pix (id, carteira, valor, creditos)
                VALUES ({id}, {carteira}, {Preco}, {Quantidade}) ON CONFLICT DO NOTHING
                """, ct);

        var pedidos = await db.Database.SqlQuery<PedidoPix>($"""
            SELECT id AS "Id", valor AS "Valor", creditos AS "Creditos", pagamento AS "Pagamento",
                   liberado AS "Liberado"
            FROM simulador_creditos_pix WHERE id = {id} AND carteira = {carteira}
            """).ToListAsync(ct);
        return pedidos.SingleOrDefault();
    }

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
