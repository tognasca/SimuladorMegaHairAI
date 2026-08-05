// SimuladorMegaHair.Infrastructure/Services/PromptBuilder.cs
namespace SimuladorMegaHair.Infrastructure.Services;

public static class PromptBuilder
{
    // ── Flux Fill / SD Inpainting ────────────────────────────

    public static string BuildInpainting(
        string comprimento, string cor, string tipoCabelo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);
        var compEn = TraduzirComprimento(comprimento);

        // Focado SÓ no cabelo — sem mencionar pessoa ou corpo
        return
            $"{corEn} {tipoEn} hair, {compEn}, " +
            "hair only, detailed individual hair strands, " +
            "natural hair texture, glossy healthy hair, " +
            "professional hair salon photography, " +
            "sharp focus, studio lighting, photorealistic, 8k";
    }

    public static string BuildInpaintingNegative() =>
        "person, face, body, skin, neck, shoulders, nude, naked, " +
        "nsfw, explicit, revealing, " +
        "cartoon, anime, illustration, painting, " +
        "blurry, deformed, ugly, watermark, text, logo, " +
        "extra fingers, bad anatomy";

    // ── OpenAI GPT Image Edit ────────────────────────────────

    public static string BuildOpenAI(
        string comprimento, string cor, string tipoCabelo, string metodo)
    {
        var corPt = cor;
        var tipoPt = tipoCabelo;
        var compEn = TraduzirComprimento(comprimento);
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);

        // OpenAI aceita prompt descritivo em inglês
        return
            $"Replace only the hair area with {corEn} {tipoEn} hair extensions, " +
            $"{compEn}. " +
            "Keep the person face, skin tone, expression and clothing exactly the same. " +
            "Natural realistic hair texture, individual strands visible, " +
            "salon quality, professional photo. " +
            "Do not change anything except the hair.";
    }

    // ── Local (descrição para log) ───────────────────────────

    public static string BuildLocal(
        string comprimento, string cor, string tipoCabelo) =>
        $"[LOCAL] {TraduzirCor(cor)} {TraduzirTipo(tipoCabelo)} hair, " +
        $"{TraduzirComprimento(comprimento)}";

    // ── Fallbacks progressivos (anti-NSFW) ──────────────────

    public static (string prompt, string negative)[] BuildFallbacks(
        string comprimento, string cor, string tipoCabelo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);
        var compEn = TraduzirComprimento(comprimento);

        return new[]
        {
            (
                prompt:
                    $"{corEn} {tipoEn} hair, {compEn}, " +
                    "hair only, natural texture, salon photo, sharp focus",
                negative:
                    BuildInpaintingNegative()
            ),
            (
                prompt:   $"{corEn} hair extensions, {compEn}, product photo",
                negative: "nsfw, nude, naked, person, face, body, cartoon, blurry"
            ),
            (
                prompt:   "hair, natural texture, studio lighting",
                negative: "nsfw, nude, person, body, cartoon"
            )
        };
    }

    // ── Tradutores ───────────────────────────────────────────

    public static string TraduzirCor(string? cor) =>
        cor?.ToLowerInvariant() switch
        {
            "preto" => "black",
            "castanho" => "dark brown",
            "chocolate" => "chocolate brown",
            "loiro" => "blonde",
            "mel" => "honey blonde",
            "ruivo" => "auburn red",
            "platinado" => "platinum blonde",
            "rosa" => "rose pink",
            "azul" => "blue",
            _ => "brown"
        };

    public static string TraduzirTipo(string? tipo) =>
        tipo?.ToLowerInvariant() switch
        {
            "liso" => "straight",
            "ondulado" => "wavy",
            "cacheado" => "curly",
            "crespo" => "coily",
            _ => "straight"
        };

    public static string TraduzirComprimento(string? comprimento) =>
        comprimento?.ToLowerInvariant() switch
        {
            "45 cm" => "shoulder length",
            "55 cm" => "chest length",
            "65 cm" => "long",
            "75 cm" => "very long",
            "85 cm" => "extra long, below waist",
            _ => "long"
        };
}