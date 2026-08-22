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
        GarantirTamanho(mascara, original.Width, original.Height);

        if (featherSigma > 0)
            mascara.Mutate(x => x.GaussianBlur(featherSigma));

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
                    if (t < 0.004f)
                        continue;

                    if (t > 0.996f)
                    {
                        origRow[x] = genRow[x];
                        continue;
                    }

                    origRow[x] = Mix(origRow[x], genRow[x], t);
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
