using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Storage;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Exclusão REAL de simulações: remove os registros do banco E os arquivos
/// de foto no disco. Antes, excluir um cliente só anulava o ClienteId das
/// simulações (DeleteBehavior.SetNull) e nenhum arquivo era apagado — a foto
/// continuava no servidor, acessível pela URL.
/// </summary>
public sealed class ExclusaoDadosService
{
    private readonly AppDbContext _db;
    private readonly ArmazenamentoMidia _midia;
    private readonly ILogger<ExclusaoDadosService> _logger;

    public ExclusaoDadosService(
        AppDbContext db, ArmazenamentoMidia midia, ILogger<ExclusaoDadosService> logger)
    {
        _db = db;
        _midia = midia;
        _logger = logger;
    }

    /// <summary>
    /// Remove as simulações e salva. Qualquer outra alteração já pendente no
    /// DbContext (ex.: remoção do cliente) é gravada na MESMA transação.
    /// Depois, apaga cada arquivo que não seja mais referenciado por nenhuma
    /// simulação restante (a mesma foto original pode ser usada por várias).
    /// </summary>
    public async Task ExcluirSimulacoesAsync(
        IReadOnlyCollection<Simulacao> simulacoes, CancellationToken ct)
    {
        var caminhos = simulacoes
            .SelectMany(s => new[] { s.FotoOriginalPath, s.FotoResultadoPath })
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        _db.Simulacoes.RemoveRange(simulacoes);
        await _db.SaveChangesAsync(ct);

        var apagados = 0;
        foreach (var caminho in caminhos)
        {
            var emUso = await _db.Simulacoes.AnyAsync(
                s => s.FotoOriginalPath == caminho || s.FotoResultadoPath == caminho, ct);

            if (!emUso && _midia.Excluir(caminho))
                apagados++;
        }

        _logger.LogInformation(
            "Exclusão: {Sims} simulação(ões) removida(s), {Arquivos} arquivo(s) apagado(s).",
            simulacoes.Count, apagados);
    }
}
