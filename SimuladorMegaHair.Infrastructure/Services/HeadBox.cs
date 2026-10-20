using SixLabors.ImageSharp;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Caixa da cabeça estimada a partir do rosto detectado (pixels da imagem preparada).
public sealed record HeadBox(
    float CenterX,
    float TopY,
    float ChinY,
    float Width,
    float Height,
    Rectangle FaceRect);