using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Cola o resultado da IA só na região da máscara e devolve
/// os pixels originais em rosto, roupa e fundo.
/// </summary>
public static class IdentityCompositor
{
    public static async Task<string> ComporPreservandoIdentidadeAsync(
        string originalPath,
        string geradaPath,
        string maskPath,
        string outputFolder,
        float featherSigma,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputFolder);

        using var original = await Image.LoadAsync<Rgba32>(originalPath, ct);
        using var gerada = await Image.LoadAsync<Rgba32>(geradaPath, ct);
        using var mascara = await Image.LoadAsync<Rgba32>(maskPath, ct);

        GarantirTamanho(gerada, original.Width, original.Height);

        // A máscara deve estar no mesmo sistema de coordenadas da imagem original.
        // Nearest-neighbor evita tons intermediários artificiais antes do feather.
        if (mascara.Width != original.Width || mascara.Height != original.Height)
        {
            mascara.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(original.Width, original.Height),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.NearestNeighbor
            }));
        }

        if (featherSigma > 0)
            mascara.Mutate(x => x.GaussianBlur(Math.Clamp(featherSigma, 0f, 12f)));

        original.ProcessPixelRows(gerada, mascara, (origAcc, genAcc, mskAcc) =>
        {
            for (int y = 0; y < origAcc.Height; y++)
            {
                var origRow = origAcc.GetRowSpan(y);
                var genRow = genAcc.GetRowSpan(y);
                var mskRow = mskAcc.GetRowSpan(y);

                for (int x = 0; x < origRow.Length; x++)
                {
                    var t = mskRow[x].R / 255f;
                    // Uma única interpolação alfa; evita mistura dupla nas bordas.
                    if (t <= 0f) continue;
                    origRow[x] = t >= 0.999f
                        ? genRow[x]
                        : Mix(origRow[x], genRow[x], t);
                }
            }
        });

        var fileName = $"{Guid.NewGuid()}.png";
        var fullPath = Path.Combine(outputFolder, fileName);
        await original.SaveAsPngAsync(fullPath, ct);
        return $"resultados/{fileName}";
    }

    private static void GarantirTamanho(Image img, int width, int height)
    {
        if (img.Width == width && img.Height == height)
            return;

        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Stretch
        }));
    }

    private static Rgba32 Mix(Rgba32 a, Rgba32 b, float t)
    {
        return new Rgba32(
            (byte)Math.Clamp(a.R + (b.R - a.R) * t, 0, 255),
            (byte)Math.Clamp(a.G + (b.G - a.G) * t, 0, 255),
            (byte)Math.Clamp(a.B + (b.B - a.B) * t, 0, 255),
            255);
    }
}
