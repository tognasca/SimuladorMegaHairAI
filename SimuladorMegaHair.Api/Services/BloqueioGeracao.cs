using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Services;

internal sealed class BloqueioGeracao(AppDbContext db, long chave) : IAsyncDisposable
{
    public static async Task<BloqueioGeracao?> TentarAsync(AppDbContext db, long chave, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var adquirido = false;
        try
        {
            await using var comando = Comando(db, "SELECT pg_try_advisory_lock(@chave)", chave);
            adquirido = await comando.ExecuteScalarAsync(ct) is true;
            return adquirido ? new(db, chave) : null;
        }
        finally
        {
            if (!adquirido) await db.Database.CloseConnectionAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var comando = Comando(db, "SELECT pg_advisory_unlock(@chave)", chave);
            await comando.ExecuteScalarAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static DbCommand Comando(AppDbContext db, string sql, long chave)
    {
        var comando = db.Database.GetDbConnection().CreateCommand();
        comando.CommandText = sql;
        var parametro = comando.CreateParameter();
        parametro.ParameterName = "chave";
        parametro.Value = chave;
        comando.Parameters.Add(parametro);
        return comando;
    }
}
