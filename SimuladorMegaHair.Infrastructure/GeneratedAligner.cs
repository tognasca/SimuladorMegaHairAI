using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// O modelo generativo pode devolver a imagem com outra proporção/escala/posição.
/// Em vez de estimar pelo rosto (que muda com o cabelo), MEDE o melhor encaixe usando só o que não é cabelo:
/// corpo e fundo (fora da zona de edição). Testa 3 geometrias (esticar, ajustar altura, ajustar largura)
/// e, para cada uma, a melhor translação; escolhe a de menor diferença de bordas.
public static class GeneratedAligner
{
    private const int Search = 10;   // ±10 px na resolução de análise (metade) = ±20 px reais

    public static async Task<string> AlinharAsync(
        string origPath, string genPath, string zonaPath,
        string outFolder, ILogger log, CancellationToken ct)
    {
        using var o = await Image.LoadAsync<Rgba32>(origPath, ct);
        using var g = await Image.LoadAsync<Rgba32>(genPath, ct);
        using var zona = await Image.LoadAsync<L8>(zonaPath, ct);
        int W = o.Width, H = o.Height, gw = g.Width, gh = g.Height;

        int aw = Math.Max(16, W / 2), ah = Math.Max(16, H / 2);
        float[] oe = Edge(o, aw, ah);
        bool[] valid = ValidMask(zona, aw, ah);

        var cands = new List<(string name, int nw, int nh, int ox, int oy)>
        {
            ("esticar", W, H, 0, 0)
        };
        {
            float s = (float)H / gh; int nw = (int)Math.Round(gw * s);
            if (nw != W) cands.Add(("altura", nw, H, (W - nw) / 2, 0));
        }
        {
            float s = (float)W / gw; int nh = (int)Math.Round(gh * s);
            if (nh != H) cands.Add(("largura", W, nh, 0, (H - nh) / 2));
        }

        double bestScore = double.MaxValue;
        int bestIdx = -1, bestTx = 0, bestTy = 0;
        Image<Rgba32>? bestImg = null;

        for (int i = 0; i < cands.Count; i++)
        {
            var c = cands[i];
            using var r = g.Clone(x => x.Resize(c.nw, c.nh));
            int gw2 = Math.Max(16, c.nw / 2), gh2 = Math.Max(16, c.nh / 2);
            float[] ge = Edge(r, gw2, gh2);
            var (score, tx, ty) = BestShift(oe, valid, aw, ah, ge, gw2, gh2, c.ox / 2, c.oy / 2);
            log.LogInformation("[ALIGN] candidato {N}: diferenca={S:F2} deslocamento=({TX},{TY}) (meia resolucao)",
                c.name, score, tx, ty);
            if (score < bestScore)
            {
                bestScore = score; bestIdx = i; bestTx = tx; bestTy = ty;
                bestImg?.Dispose();
                bestImg = r.Clone();
            }
        }

        if (bestIdx < 0 || bestImg is null)
        {
            log.LogWarning("[ALIGN] Sem pixels suficientes de corpo/fundo para medir; usando 'esticar' sem deslocamento.");
            using var fallback = g.Clone(x => x.Resize(W, H));
            var pf = Path.Combine(outFolder, $"align_{Guid.NewGuid():N}.png");
            await fallback.SaveAsPngAsync(pf, ct);
            return pf;
        }

        var best = cands[bestIdx];
        int fx = best.ox + bestTx * 2, fy = best.oy + bestTy * 2;
        log.LogInformation(
            "[ALIGN] original {W}x{H} gerada {GW}x{GH} | escolhido='{N}' deslocamento final=({FX},{FY})px",
            W, H, gw, gh, best.name, fx, fy);

        using var canvas = o.Clone();   // fundo = original (cobre bordas que a gerada não alcança)
        canvas.Mutate(c => c.DrawImage(bestImg, new Point(fx, fy), 1f));
        bestImg.Dispose();

        var path = Path.Combine(outFolder, $"align_{Guid.NewGuid():N}.png");
        await canvas.SaveAsPngAsync(path, ct);
        return path;
    }

    private static float[] Edge(Image<Rgba32> img, int w, int h)
    {
        using var s = img.Clone(c => c.Resize(w, h));
        var gray = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = s[x, y];
                gray[y * w + x] = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
            }
        var e = new float[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                e[y * w + x] = Math.Abs(gray[y * w + x + 1] - gray[y * w + x - 1])
                             + Math.Abs(gray[(y + 1) * w + x] - gray[(y - 1) * w + x]);
        return e;
    }

    // true = pixel FORA da zona de edição (corpo e fundo), longe das bordas.
    private static bool[] ValidMask(Image<L8> zona, int w, int h)
    {
        using var s = zona.Clone(c => c.Resize(w, h));
        var v = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                v[y * w + x] = s[x, y].PackedValue < 64 && x >= 4 && y >= 4 && x < w - 4 && y < h - 4;
        return v;
    }

    private static (double score, int tx, int ty) BestShift(
        float[] oe, bool[] valid, int aw, int ah,
        float[] ge, int gw, int gh, int ox, int oy)
    {
        double best = double.MaxValue; int bestTx = 0, bestTy = 0;
        for (int ty = -Search; ty <= Search; ty++)
            for (int tx = -Search; tx <= Search; tx++)
            {
                double sum = 0; int n = 0;
                for (int y = 6; y < ah - 6; y += 2)
                {
                    int gy = y - oy - ty;
                    if (gy < 3 || gy >= gh - 3) continue;
                    for (int x = 6; x < aw - 6; x += 2)
                    {
                        if (!valid[y * aw + x]) continue;
                        int gx = x - ox - tx;
                        if (gx < 3 || gx >= gw - 3) continue;
                        sum += Math.Abs(oe[y * aw + x] - ge[gy * gw + gx]);
                        n++;
                    }
                }
                if (n < 500) continue;
                double sc = sum / n;
                if (sc < best) { best = sc; bestTx = tx; bestTy = ty; }
            }
        return (best, bestTx, bestTy);
    }
}