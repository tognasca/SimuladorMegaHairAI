using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Gera máscara PNG para DALL-E 2:
/// - TRANSPARENTE = área que a IA EDITA (cabelo)
/// - OPACO        = área que a IA PRESERVA (rosto, pescoço, corpo)
/// </summary>
public static class HairMaskGenerator
{
    public static async Task<string> GerarMascaraCabeloAsync(
        string imagemOriginalPath,
        string outputFolder,
        FaceBox? rostoDetectado,
        CancellationToken cancellationToken = default)
    {
        using var imagemOriginal = await Image.LoadAsync<Rgba32>(imagemOriginalPath, cancellationToken);

        int width = imagemOriginal.Width;
        int height = imagemOriginal.Height;

        // Começa TUDO OPACO (preserva tudo por padrão no DALL-E 2)
        using var mascara = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));

        // Define área do rosto
        float centroX, centroY, raioX, raioY;
        int pescocoY1;

        if (rostoDetectado != null)
        {
            var face = rostoDetectado;
            float expandX = face.Width * 0.15f;
            float expandY = face.Height * 0.15f;

            float x1 = face.X - expandX;
            float y1 = face.Y - expandY;
            float x2 = face.X + face.Width + expandX;
            float y2 = face.Y + face.Height + expandY;

            centroX = (x1 + x2) / 2f;
            centroY = (y1 + y2) / 2f;
            raioX = (x2 - x1) / 2f;
            raioY = (y2 - y1) / 2f;
            pescocoY1 = (int)y2;
        }
        else
        {
            centroX = width * 0.5f;
            centroY = height * 0.42f;
            raioX = width * 0.22f;
            raioY = height * 0.28f;
            pescocoY1 = (int)(height * 0.65f);
        }

        int pescocoX1 = Math.Max(0, (int)(centroX - raioX * 1.2f));
        int pescocoX2 = Math.Min(width, (int)(centroX + raioX * 1.2f));

        mascara.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    // Distância normalizada ao rosto
                    float dx = (x - centroX) / raioX;
                    float dy = (y - centroY) / raioY;
                    float distOval = MathF.Sqrt(dx * dx + dy * dy);

                    // Está no rosto → OPACO (preserva)
                    if (distOval <= 1.0f)
                    {
                        row[x] = new Rgba32(255, 255, 255, 255);
                        continue;
                    }

                    // Está no pescoço/ombros → OPACO (preserva)
                    if (x >= pescocoX1 && x <= pescocoX2 && y >= pescocoY1)
                    {
                        row[x] = new Rgba32(255, 255, 255, 255);
                        continue;
                    }

                    // Área do cabelo → TRANSPARENTE (IA edita)
                    row[x] = new Rgba32(0, 0, 0, 0);
                }
            }
        });

        Directory.CreateDirectory(outputFolder);
        var maskPath = Path.Combine(outputFolder, $"mask_{Guid.NewGuid()}.png");
        await mascara.SaveAsPngAsync(maskPath, cancellationToken);

        return maskPath;
    }
    private static void LogarEstatisticasMascara(string maskPath, ILogger logger)
    {
        try
        {
            using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(maskPath);
            var totalPixels = img.Width * img.Height;
            var pixelsBrancos = 0;

            img.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < acc.Height; y++)
                {
                    var row = acc.GetRowSpan(y);
                    foreach (ref var pixel in row)
                        if (pixel.R > 128) pixelsBrancos++;
                }
            });

            var percentual = (double)pixelsBrancos / totalPixels * 100;
            logger.LogInformation(
                "Máscara: {P:F1}% da imagem será substituído",
                percentual);

            // Se > 60%, a máscara está muito grande (pode estar pegando pescoço/corpo)
            if (percentual > 60)
                logger.LogWarning(
                    "⚠️ Máscara muito grande ({P:F1}%) — risco de NSFW", percentual);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível analisar máscara");
        }
    }

    /// <summary>
    /// Máscara para Replicate/SDXL/Flux inpainting:
    /// - BRANCO = área que a IA edita (cabelo)
    /// - PRETO  = área que a IA preserva (rosto, corpo)
    /// </summary>
    /// <summary>
    /// Máscara PRECISA para Replicate/SDXL inpainting:
    /// - BRANCO = área do CABELO (topo + laterais da cabeça)
    /// - PRETO  = rosto, ombros, fundo (tudo preservado)
    /// </summary>
    public static async Task<string> GerarMascaraCabeloReplicateAsync(
        string imagemOriginalPath,
        string outputFolder,
        FaceBox? rostoDetectado,
        CancellationToken cancellationToken = default)
    {
        using var imagemOriginal = await Image.LoadAsync<Rgba32>(imagemOriginalPath, cancellationToken);

        int width = imagemOriginal.Width;
        int height = imagemOriginal.Height;

        // Começa TUDO PRETO (preserva tudo por padrão)
        using var mascara = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 255));

        // Define região do rosto
        float faceCenterX, faceCenterY, faceRaioX, faceRaioY;
        float faceTopo, faceBase;

        if (rostoDetectado != null)
        {
            var face = rostoDetectado;
            faceCenterX = face.X + face.Width / 2f;
            faceCenterY = face.Y + face.Height / 2f;
            faceRaioX = face.Width / 2f;
            faceRaioY = face.Height / 2f;
            faceTopo = face.Y;
            faceBase = face.Y + face.Height;
        }
        else
        {
            // Fallback (retrato frontal)
            faceCenterX = width * 0.5f;
            faceCenterY = height * 0.42f;
            faceRaioX = width * 0.18f;
            faceRaioY = height * 0.22f;
            faceTopo = faceCenterY - faceRaioY;
            faceBase = faceCenterY + faceRaioY;
        }

        // ═══════════════════════════════════════════════════
        // ÁREA DO CABELO — bem definida
        // ═══════════════════════════════════════════════════
        // O cabelo fica ACIMA e nas LATERAIS PRÓXIMAS da cabeça

        // Topo da área do cabelo (acima da cabeça)
        float cabeloTopo = Math.Max(0, faceTopo - faceRaioY * 1.5f);

        // Base do cabelo (até onde cai o cabelo — geralmente até o meio do peito)
        float cabeloBase = Math.Min(height, faceBase + faceRaioY * 3.5f);

        // Largura do cabelo (não pode ser muito larga)
        float cabeloLargura = faceRaioX * 3.5f;
        float cabeloEsquerda = Math.Max(0, faceCenterX - cabeloLargura);
        float cabeloDireita = Math.Min(width, faceCenterX + cabeloLargura);

        // Centro e raio do OVAL DO CABELO
        float cabeloCentroX = faceCenterX;
        float cabeloCentroY = (cabeloTopo + cabeloBase) / 2f;
        float cabeloRaioX = (cabeloDireita - cabeloEsquerda) / 2f;
        float cabeloRaioY = (cabeloBase - cabeloTopo) / 2f;

        mascara.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    // 1. Está dentro do OVAL DO CABELO?
                    float dxC = (x - cabeloCentroX) / cabeloRaioX;
                    float dyC = (y - cabeloCentroY) / cabeloRaioY;
                    float distCabelo = MathF.Sqrt(dxC * dxC + dyC * dyC);

                    bool dentroDoCabelo = distCabelo <= 1.0f;

                    // 2. Está dentro do OVAL DO ROSTO?
                    float dxF = (x - faceCenterX) / (faceRaioX * 1.15f);   // expande 15%
                    float dyF = (y - faceCenterY) / (faceRaioY * 1.15f);
                    float distRosto = MathF.Sqrt(dxF * dxF + dyF * dyF);

                    bool dentroDoRosto = distRosto <= 1.0f;

                    // 3. Está no PESCOÇO/OMBROS/COLO? (proteger)
                    bool dentroDoColo = y > faceBase + faceRaioY * 0.3f;

                    // ═══ LÓGICA FINAL ═══
                    // BRANCO (edita) só se:
                    // - Está na área do cabelo
                    // - E NÃO está no rosto
                    // - E NÃO está muito abaixo (colo/ombros)

                    if (dentroDoCabelo && !dentroDoRosto)
                    {
                        // Se está muito abaixo, protege (ombros/colo)
                        if (dentroDoColo && y > faceBase + faceRaioY * 1.5f)
                        {
                            row[x] = new Rgba32(0, 0, 0, 255); // preto = preserva
                        }
                        else
                        {
                            row[x] = new Rgba32(255, 255, 255, 255); // branco = edita
                        }
                    }
                    else
                    {
                        row[x] = new Rgba32(0, 0, 0, 255); // preto = preserva
                    }
                }
            }
        });

        Directory.CreateDirectory(outputFolder);
        var maskPath = Path.Combine(outputFolder, $"mask_{Guid.NewGuid()}.png");
        await mascara.SaveAsPngAsync(maskPath, cancellationToken);

        return maskPath;
    }
}