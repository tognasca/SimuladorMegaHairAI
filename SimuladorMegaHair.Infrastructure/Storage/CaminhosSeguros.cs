using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Infrastructure.Storage;

/// <summary>
/// Lançada quando um caminho de arquivo recebido de fora (cliente HTTP ou
/// banco de dados) não segue o formato que o próprio servidor gera.
/// </summary>
public sealed class CaminhoInvalidoException : Exception
{
    public CaminhoInvalidoException(string message) : base(message) { }
}

/// <summary>
/// Único ponto do sistema que transforma um "caminho de foto" recebido de
/// fora em um caminho real de disco.
///
/// POR QUE EXISTE: antes, o campo FotoOriginalPath vindo do cliente HTTP era
/// usado como caminho de arquivo sem validação (aceitava caminho absoluto e
/// "../"), permitindo ler qualquer arquivo do servidor e enviá-lo à Replicate.
///
/// REGRA: só são aceitos nomes que o PRÓPRIO servidor gera (GUID + extensão
/// de imagem em "uploads", GUID/volume_*.png em "resultados"). Nada é
/// concatenado a partir de texto livre: o caminho final é montado a partir de
/// pasta e arquivo já validados por lista de permissão.
/// </summary>
public static class CaminhosSeguros
{
    public const string PastaUploads = "uploads";
    public const string PastaResultados = "resultados";

    private const RegexOptions Opcoes = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex NomeUpload = new(
        @"^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}\.(jpg|jpeg|png|webp)$", Opcoes);

    private static readonly Regex NomeResultado = new(
        @"^([0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}|volume_[0-9]{3}g_[0-9a-f]{32})\.png$", Opcoes);

    /// <summary>
    /// Aceita "wwwroot/uploads/x.jpg", "uploads/x.jpg", "resultados/y.png"
    /// (com "/" ou "\"). Rejeita qualquer outra coisa: "..", caminho absoluto,
    /// unidade de disco, subpastas extras, nomes fora do padrão.
    /// </summary>
    public static bool TryNormalizar(string? valor, out string pasta, out string arquivo)
    {
        pasta = string.Empty;
        arquivo = string.Empty;

        if (string.IsNullOrWhiteSpace(valor) || valor.Length > 260)
            return false;

        var v = valor.Trim().Replace('\\', '/');

        if (v.Contains("..") || v.Contains(':') || v.Contains('\0') || v.Contains('%'))
            return false;

        var partes = v.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var inicio = partes.Length > 0 &&
                     partes[0].Equals("wwwroot", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        if (partes.Length - inicio != 2)
            return false;

        var p = partes[inicio].ToLowerInvariant();
        var a = partes[inicio + 1].ToLowerInvariant();

        var valido =
            (p == PastaUploads && NomeUpload.IsMatch(a)) ||
            (p == PastaResultados && NomeResultado.IsMatch(a));

        if (!valido)
            return false;

        pasta = p;
        arquivo = a;
        return true;
    }

    /// <summary>
    /// Forma canônica gravada no banco (mantém compatibilidade com os
    /// registros existentes: uploads → "wwwroot/uploads/x", resultados →
    /// "resultados/x").
    /// </summary>
    public static string Canonico(string pasta, string arquivo) =>
        pasta == PastaUploads ? $"wwwroot/{pasta}/{arquivo}" : $"{pasta}/{arquivo}";

    /// <summary>
    /// Resolve para o caminho absoluto em disco, garantindo que o arquivo
    /// está dentro de wwwroot/{pasta}. Lança CaminhoInvalidoException se o
    /// valor for inválido e FileNotFoundException se o arquivo não existir.
    /// A mensagem nunca inclui o caminho do servidor.
    /// </summary>
    public static string ResolverExistente(
        string contentRoot, string? valor, bool apenasUploads = false)
    {
        if (!TryNormalizar(valor, out var pasta, out var arquivo) ||
            (apenasUploads && pasta != PastaUploads))
        {
            throw new CaminhoInvalidoException("Caminho de imagem inválido.");
        }

        var baseDir = Path.GetFullPath(Path.Combine(contentRoot, "wwwroot", pasta));
        var abs = Path.GetFullPath(Path.Combine(baseDir, arquivo));

        // Defesa em profundidade: mesmo com o nome já validado por regex.
        if (!abs.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new CaminhoInvalidoException("Caminho de imagem inválido.");

        if (!File.Exists(abs))
            throw new FileNotFoundException("Imagem não encontrada.");

        return abs;
    }
}
