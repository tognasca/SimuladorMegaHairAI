using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Domain.Models;

public sealed class SimulacaoRequest
{
    public required string ImagemOriginalPath { get; init; }
    public required string Comprimento { get; init; }
    public required string Cor { get; init; }
    public required string TipoCabelo { get; init; }
    public required string MetodoMegaHair { get; init; }
    public ImageProvider Provider { get; init; } = ImageProvider.Local;
}