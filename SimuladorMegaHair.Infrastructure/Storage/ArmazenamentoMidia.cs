using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace SimuladorMegaHair.Infrastructure.Storage;

/// <summary>
/// Exclusão de arquivos de mídia (fotos originais e resultados). Só apaga
/// arquivos cujo caminho passe por <see cref="CaminhosSeguros"/>.
/// </summary>
public sealed class ArmazenamentoMidia
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ArmazenamentoMidia> _logger;

    public ArmazenamentoMidia(IWebHostEnvironment env, ILogger<ArmazenamentoMidia> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>Retorna true se o arquivo existia e foi apagado.</summary>
    public bool Excluir(string? valor)
    {
        if (!CaminhosSeguros.TryNormalizar(valor, out var pasta, out var arquivo))
            return false;

        var abs = Path.Combine(_env.ContentRootPath, "wwwroot", pasta, arquivo);

        try
        {
            if (!File.Exists(abs)) return false;
            File.Delete(abs);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível excluir o arquivo de mídia {Pasta}/{Arquivo}.", pasta, arquivo);
            return false;
        }
    }
}
