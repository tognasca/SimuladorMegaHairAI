using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class PromptBuilder
{
    public static string BuildInpainting(
        string comprimento,
        string cor,
        string tipoCabelo,
        HairEditMode modo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);
        var compEn = TraduzirComprimento(comprimento);

        return modo switch
        {
            HairEditMode.Shorten =>
                $"{corEn} hair, {compEn}, {tipoEn} haircut, " +
                $"replace all hair with {corEn} {tipoEn} {compEn}, " +
                $"solid {corEn} color from roots to tips, no long hair, no hair on shoulders, " +
                "clean nape, natural hairline, realistic individual strands, photorealistic",

            HairEditMode.Recolor =>
                $"{corEn} hair, recolor all hair to {corEn}, " +
                $"uniform {corEn} color from roots to tips, no other hair color, " +
                $"healthy {tipoEn} texture, realistic strands, soft shine, photorealistic",

            _ => // Extend / Mega Hair
                $"{corEn} hair, pure {corEn} color, " +
                $"{corEn} {tipoEn} mega hair extensions, {compEn}, " +
                $"uniform {corEn} color from roots to tips, " +
                "seamless blend with natural hair, realistic density, " +
                "individual strands, salon quality, photorealistic"
        };
    }

    public static string BuildInpaintingNegative(string? corOriginal = null)
    {
        var neg = "different person, altered face, deformed face, " +
                  "wig, helmet hair, plastic hair, wax hair, " +
                  "two-tone hair, patchy color, faded roots, " +
                  "blurry, cartoon, illustration, CGI, 3d render";

        if (!string.IsNullOrWhiteSpace(corOriginal))
        {
            var corAntigaEn = TraduzirCor(corOriginal);
            neg = $"{corAntigaEn} hair, {corAntigaEn} strands, " + neg;
        }

        return neg;
    }

    public static IEnumerable<(string prompt, string negative)> BuildFallbacks(
        string comprimento, string cor, string tipoCabelo, HairEditMode modo)
    {
        var p1 = BuildInpainting(comprimento, cor, tipoCabelo, modo);
        var neg = BuildInpaintingNegative();

        yield return (p1, neg);

        // Fallback 2: Mais direto e imperativo
        yield return (
            $"realistic {TraduzirCor(cor)} {TraduzirTipo(tipoCabelo)} hair, " +
            $"{TraduzirComprimento(comprimento)}, solid {TraduzirCor(cor)} color, photorealistic hair only",
            neg);
    }

    public static string BuildOpenAI(string comprimento, string cor, string tipoCabelo, string metodo)
    {
        return BuildInpainting(comprimento, cor, tipoCabelo, HairEditMode.Extend);
    }

    public static string BuildLocal(string comprimento, string cor, string tipoCabelo)
    {
        return BuildInpainting(comprimento, cor, tipoCabelo, HairEditMode.Extend);
    }

    // --- TRADUÇÕES DE ALTO IMPACTO PARA FLUX FILL ---

    private static string TraduzirCor(string cor) => cor?.Trim().ToLowerInvariant() switch
    {
        "preto" or "black" => "jet black",
        "castanho escuro" => "dark chocolate brown",
        "castanho" or "castanho medio" => "medium chestnut brown",
        "castanho claro" => "light brown",
        "loiro escuro" => "dark blonde",
        "loiro" or "loiro medio" => "golden blonde",
        "loiro claro" or "platinado" => "platinum blonde",
        "ruivo" => "vibrant auburn red",
        "vermelho" => "vivid red",
        "iluminado" or "luzes" or "morena iluminada" => "brown hair with warm blonde highlights",
        _ => "dark brown"
    };

    private static string TraduzirComprimento(string c) => c?.Trim().ToLowerInvariant() switch
    {
        "curto" or "short" => "short hair above the shoulders, pixie-to-chin length",
        "medio" or "médio" or "medium" => "shoulder-length hair",
        "longo" or "long" => "long hair down to mid-back",
        "extra longo" or "extra-longo" => "very long waist-length hair",
        _ => "long hair to mid-back"
    };

    private static string TraduzirTipo(string t) => t?.Trim().ToLowerInvariant() switch
    {
        "liso" or "straight" => "straight",
        "ondulado" or "wavy" => "wavy",
        "cacheado" or "curly" => "curly",
        "crespo" or "coily" => "coily",
        _ => "straight"
    };
}