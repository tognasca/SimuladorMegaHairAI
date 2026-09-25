
namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class ReplicateOptions
{
    public const string Section = "Replicate";

    public string ApiToken { get; set; } = "";
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
    public int HairSegmentMaxPollAttempts { get; set; } = 20;
    public int HairSegmentPollIntervalMs { get; set; } = 1500;

    // ── Flux / Inferência atual ──────────────────────────────

    public int FluxSteps { get; set; } = 28;
    public double FluxGuidance { get; set; } = 30;
    public int ImageSize { get; set; } = 1024;

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