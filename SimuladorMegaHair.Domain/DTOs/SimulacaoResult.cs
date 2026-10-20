namespace SimuladorMegaHair.Domain.Models;

public sealed class SimulacaoResult
{
    public required string ImagemResultadoPath { get; init; }
    public required string ProviderUtilizado { get; init; }
    public required long TempoProcessamentoMs { get; init; }
    public string? Aviso { get; init; }
}