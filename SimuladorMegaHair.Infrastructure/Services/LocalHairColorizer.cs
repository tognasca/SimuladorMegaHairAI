using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Aplica cor no cabelo usando apenas processamento local (grátis).
/// Qualidade básica — apenas colore a área da máscara.
/// </summary>
public static class LocalHairColorizer
{
    public static async Task<string> AplicarCorAsync(
        string imagemPath,
        string maskPath,
        string cor,
        string outputFolder,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);

        var corRgb = ObterCorRgb(cor);

        using var imagem = await Image.LoadAsync<Rgba32>(imagemPath, ct);
        using var mascara = await Image.LoadAsync<Rgba32>(maskPath, ct);

        // Redimensiona máscara se necessário
        if (mascara.Width != imagem.Width || mascara.Height != imagem.Height)
        {
            mascara.Mutate(x => x.Resize(imagem.Width, imagem.Height));
        }

        imagem.ProcessPixelRows(mascara, (imgAcc, mskAcc) =>
        {
            for (int y = 0; y < imgAcc.Height; y++)
            {
                var imgRow = imgAcc.GetRowSpan(y);
                var mskRow = mskAcc.GetRowSpan(y);

                for (int x = 0; x < imgRow.Length; x++)
                {
                    // Área branca da máscara = onde aplicar cor
                    var alpha = mskRow[x].R / 255f;
                    if (alpha < 0.1f) continue;

                    // Mistura mantendo a luminosidade original
                    ref var pixel = ref imgRow[x];
                    var luminance = (pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f) / 255f;

                    pixel.R = (byte)Math.Clamp(corRgb.R * luminance * alpha + pixel.R * (1 - alpha), 0, 255);
                    pixel.G = (byte)Math.Clamp(corRgb.G * luminance * alpha + pixel.G * (1 - alpha), 0, 255);
                    pixel.B = (byte)Math.Clamp(corRgb.B * luminance * alpha + pixel.B * (1 - alpha), 0, 255);
                }
            }
        });

        var fileName = $"{Guid.NewGuid()}.png";
        var fullPath = Path.Combine(outputFolder, fileName);
        await imagem.SaveAsPngAsync(fullPath, ct);

        return $"resultados/{fileName}";
    }

    private static Rgba32 ObterCorRgb(string cor) =>
        cor?.ToLowerInvariant() switch
        {
            "preto" => new Rgba32(20, 15, 10),
            "castanho" => new Rgba32(80, 50, 30),
            "chocolate" => new Rgba32(60, 35, 20),
            "loiro" => new Rgba32(220, 180, 120),
            "mel" => new Rgba32(200, 150, 80),
            "ruivo" => new Rgba32(180, 80, 40),
            "platinado" => new Rgba32(240, 230, 210),
            _ => new Rgba32(100, 70, 40)
        };
}