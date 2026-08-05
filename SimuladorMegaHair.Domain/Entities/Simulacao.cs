// SimuladorMegaHair.Domain/Entities/Simulacao.cs
namespace SimuladorMegaHair.Domain.Entities;

public class Simulacao
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── FK e navegação ──────────────────────────────────────
    public Guid? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }   // ✅ Navegação

    // ── Dados da simulação ──────────────────────────────────
    public string FotoOriginalPath { get; set; } = string.Empty;
    public string FotoResultadoPath { get; set; } = string.Empty;
    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;
    public string PromptUsado { get; set; } = string.Empty;
    public decimal ValorEstimado { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    // ── Rastreamento do provider ────────────────────────────
    public string? ProviderUtilizado { get; set; }
    public long? TempoProcessamentoMs { get; set; }
}