namespace SimuladorMegaHair.Domain.Entities;

public class Simulacao
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public string FotoOriginalPath { get; set; } = string.Empty;
    public string? FotoResultadoPath { get; set; }

    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;

    public string PromptUsado { get; set; } = string.Empty;
    public decimal? ValorEstimado { get; set; }

    public bool ClienteGostou { get; set; } = false;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}