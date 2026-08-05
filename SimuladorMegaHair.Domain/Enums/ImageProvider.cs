// SimuladorMegaHair.Domain/Enums/ImageProvider.cs
namespace SimuladorMegaHair.Domain.Enums;

public enum ImageProvider
{
    /// Gratuito — roda local com ONNX
    Local = 0,

    /// Pago — Replicate (Flux Fill + InsightFace + CodeFormer)
    Replicate = 1,

    /// Pago — OpenAI GPT-4o Image Edit
    OpenAI = 2
}