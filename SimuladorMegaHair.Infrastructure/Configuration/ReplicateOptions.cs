// SimuladorMegaHair.Infrastructure/Configuration/ReplicateOptions.cs
namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class ReplicateOptions
{
    public const string Section = "Replicate";

    public string ApiToken { get; set; } = string.Empty;

    // ── Modelos ──────────────────────────────────────────────
    public string FluxFillOwner { get; set; } = "zsxkib";
    public string FluxFillName { get; set; } = "flux-fill";

    public string InsightFaceOwner { get; set; } = "zsxkib";
    public string InsightFaceName { get; set; } = "instant-id";

    public string CodeFormerOwner { get; set; } = "sczhou";
    public string CodeFormerName { get; set; } = "codeformer";

    // ── Inferência ───────────────────────────────────────────
    public int FluxSteps { get; set; } = 28;
    public double FluxGuidance { get; set; } = 22;
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