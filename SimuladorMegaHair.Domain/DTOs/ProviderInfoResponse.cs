// SimuladorMegaHair.Domain/DTOs/ProviderInfoResponse.cs
namespace SimuladorMegaHair.Domain.DTOs;

public sealed class ProviderInfoResponse
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public bool Gratuito { get; set; }
    public bool Habilitado { get; set; }
    public bool Padrao { get; set; }
}