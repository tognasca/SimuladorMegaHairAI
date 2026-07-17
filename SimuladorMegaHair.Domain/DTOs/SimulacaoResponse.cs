namespace SimuladorMegaHair.Domain.DTOs;

public class SimulacaoResponse
{
    public Guid Id { get; set; }
    public string FotoOriginalUrl { get; set; } = string.Empty;
    public string FotoResultadoUrl { get; set; } = string.Empty;
    public decimal? ValorEstimado { get; set; }
}