namespace SimuladorMegaHair.Domain.Models;

public class AjustarVolumeRequest
{
    public int Nivel { get; set; } = 2; // 1 = 100g, 2 = 200g, 3 = 300g, 4 = 400g
    public string? ImagemOriginalPath { get; set; }
    public string? ImagemResultadoPath { get; set; }

    /// <summary>
    /// Comprimento desejado (ex: "65 cm"), usado para calibrar a máscara de
    /// cabelo que restringe o efeito de volume. Preenchido automaticamente
    /// pelo controller a partir da simulação salva.
    /// </summary>
    public string? Comprimento { get; set; }

    public AjustarVolumeRequest() { }

    public AjustarVolumeRequest(int nivel)
    {
        Nivel = nivel;
    }
}

// ✅ ALIAS / COMPATIBILIDADE: Evita erros se algum arquivo antigo ainda chamar pelo nome em inglês
public class VolumeAdjustmentRequest : AjustarVolumeRequest
{
    public VolumeAdjustmentRequest() { }
    public VolumeAdjustmentRequest(int nivel) : base(nivel) { }
    public int NivelGramas => Nivel * 100;
}