using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Composição com comportamento conhecido:
///   saída = original onde a zona é 0  |  gerada onde a zona é 255  |  mistura só nas bordas já suavizadas da zona.
/// Não aplica nenhum desfoque adicional. O rosto (buraco da zona) fica byte a byte igual ao original,
/// e o resultado informa quantos pixels do núcleo do rosto diferem do original (esperado: 0).
public static class ComposicaoSegura
{
    public sealed record Resultado(string CaminhoAbsoluto, int PixelsAlteradosNoRosto, double AreaColadaPct);

    public static async Task<Resultado> ComporAsync(
        string basePath, string genPath, string zonaPath, Rectangle rosto,
        string outFolder, CancellationToken ct, string? nomeArquivo = null)
    {
        using var b = await Image.LoadAsync<Rgba32>(basePath, ct);
        using var g = await Image.LoadAsync<Rgba32>(genPath, ct);
        using var z = await Image.LoadAsync<L8>(zonaPath, ct);
        int W = b.Width, H = b.Height;
        if (g.Width != W || g.Height != H) g.Mutate(c => c.Resize(W, H));
        if (z.Width != W || z.Height != H) z.Mutate(c => c.Resize(W, H));

        using var o = b.Clone();
        long colados = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float a = z[x, y].PackedValue / 255f;
                if (a <= 0f) continue;
                if (a > 0.5f) colados++;
                var p = b[x, y]; var q = g[x, y];
                o[x, y] = new Rgba32(
                    (byte)(p.R + (q.R - p.R) * a),
                    (byte)(p.G + (q.G - p.G) * a),
                    (byte)(p.B + (q.B - p.B) * a), 255);
            }

        // Verificação de identidade: núcleo do rosto (60% central da caixa) deve ser idêntico ao original.
        int x0 = Math.Max(0, rosto.X + (int)(rosto.Width * 0.2)), x1 = Math.Min(W - 1, rosto.X + (int)(rosto.Width * 0.8));
        int y0 = Math.Max(0, rosto.Y + (int)(rosto.Height * 0.2)), y1 = Math.Min(H - 1, rosto.Y + (int)(rosto.Height * 0.8));
        int alterados = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = b[x, y]; var q = o[x, y];
                if (p.R != q.R || p.G != q.G || p.B != q.B) alterados++;
            }

        Directory.CreateDirectory(outFolder);
        var path = Path.Combine(outFolder, nomeArquivo ?? $"{Guid.NewGuid()}.png");
        await o.SaveAsPngAsync(path, ct);
        return new Resultado(path, alterados, colados / (double)((long)W * H));
    }
}