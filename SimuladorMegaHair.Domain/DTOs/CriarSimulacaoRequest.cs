namespace SimuladorMegaHair.Domain.DTOs;

using SimuladorMegaHair.Domain.Enums;
public class CriarSimulacaoRequest
{
    public Guid? ClienteId { get; set; }
    public string FotoOriginalPath { get; set; } = string.Empty;
    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;

    /// Provider de IA (Local=grátis, Replicate/OpenAI=pago)
    public ImageProvider Provider { get; set; } = ImageProvider.Local;
}