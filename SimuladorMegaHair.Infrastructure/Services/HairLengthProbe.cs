using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Mede onde termina o cabelo gerado: compara a imagem gerada (já alinhada) com a original,
/// dentro da zona de edição. Devolve a última linha com diferença e as colunas onde o cabelo
/// chega perto dessa linha (usadas para continuar os fios até a borda da foto).
public static class HairLengthProbe
{
    public sealed record Medicao(int? Fim, bool[]? Colunas, int Largura, int Altura);

    public static async Task<Medicao> MedirAsync(string origPath, string genAlignedPath, string zonaPath, CancellationToken ct)
    {
        using var o = await Image.LoadAsync<Rgba32>(origPath, ct);
        using var g = await Image.LoadAsync<Rgba32>(genAlignedPath, ct);
        using var z = await Image.LoadAsync<L8>(zonaPath, ct);
        int W = o.Width, H = o.Height;
        int aw = Math.Max(16, W / 2), ah = Math.Max(16, H / 2);
        o.Mutate(c => c.Resize(aw, ah).GaussianBlur(1.5f));
        g.Mutate(c => c.Resize(aw, ah).GaussianBlur(1.5f));
        z.Mutate(c => c.Resize(aw, ah));

        var changed = new bool[aw * ah];
        for (int y = 0; y < ah; y++)
            for (int x = 0; x < aw; x++)
            {
                if (z[x, y].PackedValue <= 127) continue;
                var a = o[x, y]; var b = g[x, y];
                float ga = 0.299f * a.R + 0.587f * a.G + 0.114f * a.B;
                float gb = 0.299f * b.R + 0.587f * b.G + 0.114f * b.B;
                changed[y * aw + x] = Math.Abs(ga - gb) > 30f;
            }

        int minCount = Math.Max(4, (int)(aw * 0.012));
        int ultima = -1;
        for (int y = 0; y < ah; y++)
        {
            int n = 0;
            for (int x = 0; x < aw; x++) if (changed[y * aw + x]) n++;
            if (n >= minCount) ultima = y;
        }
        if (ultima < 0) return new Medicao(null, null, W, H);

        // colunas com cabelo perto do fim (faixa de ~6% da altura acima da última linha)
        int band = Math.Max(6, (int)(ah * 0.06));
        var col = new bool[aw];
        for (int x = 0; x < aw; x++)
        {
            int n = 0;
            for (int y = Math.Max(0, ultima - band); y <= ultima; y++) if (changed[y * aw + x]) n++;
            col[x] = n >= 2;
        }
        col = Erodir(Dilatar(col, 6), 6);   // fecha buracos de até ~6 colunas
        col = Dilatar(col, 2);              // folga lateral

        var full = new bool[W];
        for (int x = 0; x < W; x++) full[x] = col[Math.Min(aw - 1, x * aw / W)];
        return new Medicao(ultima * 2, full, W, H);
    }

    private static bool[] Dilatar(bool[] a, int r)
    {
        var o = new bool[a.Length];
        for (int i = 0; i < a.Length; i++)
            for (int k = Math.Max(0, i - r); k <= Math.Min(a.Length - 1, i + r); k++)
                if (a[k]) { o[i] = true; break; }
        return o;
    }

    private static bool[] Erodir(bool[] a, int r)
    {
        var o = new bool[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            bool todos = true;
            for (int k = i - r; k <= i + r; k++)
                if (k < 0 || k >= a.Length || !a[k]) { todos = false; break; }
            o[i] = todos;
        }
        return o;
    }
}