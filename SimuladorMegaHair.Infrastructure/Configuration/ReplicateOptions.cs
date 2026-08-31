//// SimuladorMegaHair.Infrastructure/Configuration/ReplicateOptions.cs
//namespace SimuladorMegaHair.Infrastructure.Configuration;

//public sealed class ReplicateOptions
//{
//    public const string Section = "Replicate";

//    public string ApiToken { get; set; } = string.Empty;

//    // ── Modelos ──────────────────────────────────────────────
//    public string FluxFillOwner { get; set; } = "zsxkib";
//    public string FluxFillName { get; set; } = "flux-fill";

//    public string InsightFaceOwner { get; set; } = "zsxkib";
//    public string InsightFaceName { get; set; } = "instant-id";

//    public string CodeFormerOwner { get; set; } = "sczhou";
//    public string CodeFormerName { get; set; } = "codeformer";

//    // ── Inferência ───────────────────────────────────────────
//    public int FluxSteps { get; set; } = 28;
//    public double FluxGuidance { get; set; } = 22;
//    public int ImageSize { get; set; } = 1024;

//    /// <summary>
//    /// Raio do blur na máscara antes do composite (borda do cabelo).
//    /// </summary>
//    public double MaskFeatherSigma { get; set; } = 4;

//    // ── Polling ──────────────────────────────────────────────
//    public int MaxPollAttempts { get; set; } = 90;
//    public int PollIntervalMs { get; set; } = 2000;

//    // ── Retry ────────────────────────────────────────────────
//    public int MaxRateLimitRetries { get; set; } = 5;
//    public int RateLimitBaseDelayMs { get; set; } = 11000;
//    public int RateLimitMaxDelayMs { get; set; } = 60000;
//}

// SimuladorMegaHair.Infrastructure/Configuration/ReplicateOptions.cs
namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class ReplicateOptions
{
    public const string Section = "Replicate";

    public string ApiToken { get; set; } = string.Empty;

    // ── Modelos ──────────────────────────────────────────────

    //public string FluxFillOwner { get; set; } = "zsxkib";
    //public string FluxFillName { get; set; } = "flux-fill";]


    public string FluxFillOwner { get; set; } = "black-forest-labs";
    public string FluxFillName { get; set; } = "flux-fill-dev";
    public string InsightFaceOwner { get; set; } = "zsxkib";
    public string InsightFaceName { get; set; } = "instant-id";

    public string CodeFormerOwner { get; set; } = "sczhou";
    public string CodeFormerName { get; set; } = "codeformer";

    // Novo: Inpaint para troca/geração do cabelo
    public string InpaintOwner { get; set; } = "zf-kbot";
    public string InpaintName { get; set; } = "inpaint-and-guess-prompt";

    // ── Segmentação de cabelo via IA real (Grounded SAM) ──────
    // Substitui a antiga máscara 100% geométrica/heurística por um
    // modelo de segmentação de verdade, treinado, hospedado no Replicate.
    // Confirmado em uso de produção (doiwear.it) e listado na coleção
    // oficial de detecção/segmentação do Replicate.
    // Fonte: https://replicate.com/schananas/grounded_sam
    public string HairSegmentOwner { get; set; } = "schananas";
    public string HairSegmentName { get; set; } = "grounded_sam";

    /// <summary>
    /// Se a segmentação por IA falhar ou expirar, a aplicação cai
    /// automaticamente para a máscara geométrica (nunca quebra o fluxo).
    /// </summary>
    public int HairSegmentMaxPollAttempts { get; set; } = 20;
    public int HairSegmentPollIntervalMs { get; set; } = 1500;

    // ── Flux / Inferência atual ──────────────────────────────

    public int FluxSteps { get; set; } = 28;
    public double FluxGuidance { get; set; } = 30;
    public int ImageSize { get; set; } = 1024;

    // ── Inpaint ──────────────────────────────────────────────

    /// <summary>
    /// Número de passos utilizados pelo modelo de inpainting.
    /// </summary>
    //public int InpaintSteps { get; set; } = 25;

    ///// <summary>
    ///// Guidance/CFG utilizado pelo modelo de inpainting.
    ///// </summary>
    //public double InpaintGuidance { get; set; } = 5.0;

    ///// <summary>
    ///// Força da alteração dentro da área mascarada.
    ///// 1.0 = maior liberdade para reconstruir o cabelo.
    ///// </summary>
    //public double InpaintStrength { get; set; } = 0.9;

    ///// <summary>
    ///// Expande a máscara antes do inpainting.
    ///// Útil para integrar a raiz e as bordas do mega hair.
    ///// </summary>
    //public int InpaintGrowSize { get; set; } = 2;

    ///// <summary>
    ///// Força utilizada para preservar/integrar as bordas da máscara.
    ///// </summary>
    //public double InpaintEdgeStrength { get; set; } = 0.55;

    ///// <summary>
    ///// Força utilizada para preservar a cor original do cabelo.
    ///// </summary>
    //public double InpaintColorStrength { get; set; } = 0.55;

    ///// <summary>
    ///// Tipo de predição utilizado pelo modelo.
    ///// </summary>
    //public string InpaintPredictType { get; set; } = "standard";

    ///// <summary>
    ///// Sampler do modelo de inpainting.
    ///// </summary>
    //public string InpaintSampler { get; set; } = "euler_ancestral";

    ///// <summary>
    ///// Scheduler do modelo de inpainting.
    ///// </summary>
    //public string InpaintScheduler { get; set; } = "karras";

    ///// <summary>
    ///// Seed utilizada na geração.
    ///// 0 pode ser tratado pela aplicação como seed aleatória.
    ///// </summary>
    //public int InpaintSeed { get; set; } = 0;

    // ── Máscara / Composição ─────────────────────────────────

    /// <summary>
    /// Raio do blur na máscara antes do composite (borda do cabelo).
    /// </summary>
    public double MaskFeatherSigma { get; set; } = 4;

    // ── Polling ──────────────────────────────────────────────

    public int MaxPollAttempts { get; set; } = 90;
    public int PollIntervalMs { get; set; } = 2000;

    // ── Retry ────────────────────────────────────────────────

    public int MaxRateLimitRetries { get; set; } = 5;
    public int RateLimitBaseDelayMs { get; set; } = 11000;
    public int RateLimitMaxDelayMs { get; set; } = 60000;
}