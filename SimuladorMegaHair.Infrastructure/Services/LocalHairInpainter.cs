namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Stub — implementação futura com Stable Diffusion local via ONNX.
/// Por enquanto sempre lança para cair no LocalHairColorizer.
/// </summary>
public static class LocalHairInpainter
{
    public static Task<string> AplicarCabeloAsync(
        string imagemPath,
        string maskPath,
        string cor,
        string tipoCabelo,
        string comprimento,
        string outputFolder,
        CancellationToken ct)
    {
        throw new NotImplementedException(
            "Inpainting local ainda não implementado — usando colorização básica.");
    }
}