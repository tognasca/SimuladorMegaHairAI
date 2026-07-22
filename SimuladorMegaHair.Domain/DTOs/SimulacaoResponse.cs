namespace SimuladorMegaHair.Domain.DTOs;

public class SimulacaoResponse
{
    public Guid Id { get; set; }
    public string FotoOriginalUrl { get; set; } = string.Empty;
    public string FotoResultadoUrl { get; set; } = string.Empty;
    public decimal? ValorEstimado { get; set; }

    // Novos campos para exibir no histórico
    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}