using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class ImagePreparer
{
    /// <summary>
    /// Prepara imagem para OpenAI: converte para PNG quadrado.
    /// DALL-E 2 exige tamanhos: 256, 512 ou 1024 px quadrados.
    /// </summary>
    public static async Task<string> PrepararParaOpenAiAsync(
        string inputPath,
        string outputFolder,
        int tamanho = 1024,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(inputPath, cancellationToken);

        // Faz crop central quadrado e redimensiona
        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(tamanho, tamanho),
                Mode = ResizeMode.Max,
                Position = AnchorPositionMode.Center
            });
        });

        Directory.CreateDirectory(outputFolder);
        var outputPath = Path.Combine(outputFolder, $"prep_{Guid.NewGuid()}.png");
        await image.SaveAsPngAsync(outputPath, cancellationToken);

        return outputPath;
    }
}