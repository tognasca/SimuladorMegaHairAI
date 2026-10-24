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

    public HairEditMode Modo { get; set; } = HairEditMode.Extend;

    /// Provider de IA (Local=grátis, Replicate/OpenAI=pago)
    public ImageProvider Provider { get; set; } = ImageProvider.OpenAI;

    /// <summary>
    /// Quando true, ignora o cache de simulações iguais (foto + parâmetros)
    /// para permitir "gerar novamente".
    /// </summary>
    public bool ForcarNovaGeracao { get; set; }
}