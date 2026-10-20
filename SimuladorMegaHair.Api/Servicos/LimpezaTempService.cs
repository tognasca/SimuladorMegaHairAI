using Microsoft.Extensions.Options;
using SimuladorMegaHair.Infrastructure.Configuration;

namespace SimuladorMegaHair.Api.Servicos;

/// <summary>
/// Apaga periodicamente arquivos temporários (wwwroot/temp e wwwroot/masks)
/// que ficaram para trás quando uma geração falhou. Esses arquivos contêm
/// fotos de clientes: antes, só eram removidos no caminho de sucesso e se
/// acumulavam para sempre (o repositório trazia 22 deles).
/// </summary>
public sealed class LimpezaTempService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    private readonly IWebHostEnvironment _env;
    private readonly SimulacaoOptions _opcoes;
    private readonly ILogger<LimpezaTempService> _logger;

    public LimpezaTempService(
        IWebHostEnvironment env, IOptions<SimulacaoOptions> opcoes, ILogger<LimpezaTempService> logger)
    {
        _env = env;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                LimparPasta("temp", TimeSpan.FromMinutes(Math.Max(5, _opcoes.RetencaoTempMinutos)));
                // máscaras/auditoria mostram o rosto: nunca ficam mais que 24 h
                LimparPasta("masks", TimeSpan.FromHours(24));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha na limpeza periódica de arquivos temporários.");
            }

            try { await Task.Delay(Intervalo, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void LimparPasta(string nome, TimeSpan idadeMinima)
    {
        var pasta = Path.Combine(_env.ContentRootPath, "wwwroot", nome);
        if (!Directory.Exists(pasta)) return;

        var limite = DateTime.UtcNow - idadeMinima;
        var apagados = 0;

        foreach (var arquivo in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(arquivo) < limite)
                {
                    File.Delete(arquivo);
                    apagados++;
                }
            }
            catch (IOException) { /* em uso: tenta no próximo ciclo */ }
            catch (UnauthorizedAccessException) { }
        }

        if (apagados > 0)
            _logger.LogInformation("Limpeza: {N} arquivo(s) antigo(s) removido(s) de {Pasta}.", apagados, nome);
    }
}
