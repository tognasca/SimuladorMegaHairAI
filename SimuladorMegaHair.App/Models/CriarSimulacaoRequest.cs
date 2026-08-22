namespace SimuladorMegaHair.App.Models;

public class CriarSimulacaoRequest
{
    public Guid? ClienteId { get; set; }
    public string FotoOriginalPath { get; set; } = string.Empty;
    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;
    public string Provider { get; set; } = "Replicate";
}