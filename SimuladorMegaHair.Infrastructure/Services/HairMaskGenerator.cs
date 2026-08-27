using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Models;
using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class HairMaskGenerator
{
    public static async Task<(string maskPath, HairEditMode modoUtilizado)> GerarMascaraInteligenteAsync(
        string imagemOriginalPath,
        string outputFolder,
        FaceBox? rostoDetectado,
        string comprimentoDesejado,
        CancellationToken ct = default)
    {
        using var img = await Image.LoadAsync<Rgba32>(imagemOriginalPath, ct);
        int w = img.Width;
        int h = img.Height;

        // ── 1. Geometria do Rosto ─────────────────────────────
        float fx, fy, frx, fry, chinY;
        if (rostoDetectado != null)
        {
            fx = rostoDetectado.X + rostoDetectado.Width / 2f;
            fy = rostoDetectado.Y + rostoDetectado.Height / 2f;
            frx = Math.Max(1f, rostoDetectado.Width / 2f);
            fry = Math.Max(1f, rostoDetectado.Height / 2f);
            chinY = rostoDetectado.Y + rostoDetectado.Height;
        }
        else
        {
            fx = w * 0.50f;
            fy = h * 0.36f;
            frx = w * 0.14f;
            fry = h * 0.19f;
            chinY = fy + fry;
        }

        int cm = ExtrairCm(comprimentoDesejado);
        bool cabeloAtualLongo = DetectarSeCabeloLongoAtual(img, fx, chinY, frx, fry);
        var modo = ResolverModo(cm, cabeloAtualLongo);

        // Extensão vertical conforme os cm do mega hair
        float quedaFator = cm switch
        {
            <= 35 => 1.0f,
            <= 55 => 2.5f,
            <= 75 => 4.2f,
            _ => 5.2f // 85cm
        };

        float hairTop = Math.Max(0, fy - fry * 1.5f); // Pega topo da cabeça e atrás
        float hairBottomMax = Math.Min(h - 1f, chinY + fry * quedaFator);

        // ── 2. Proteção Rígida (Rosto, Decote e Roupa) ───────
        float protectRx = frx * 0.92f;
        float protectRy = fry * 1.05f;
        float chinProtect = chinY + fry * 0.15f; // Queixo/Pescoço superior

        using var mask = new Image<Rgba32>(w, h, new Rgba32(0, 0, 0, 255));

        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    // 🔒 ZONA 1: PROTEÇÃO TOTAL DO ROSTO E MAKE
                    float pdx = (x - fx) / protectRx;
                    float pdy = (y - fy) / protectRy;
                    if ((pdx * pdx + pdy * pdy) <= 1.0f && y <= chinProtect)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // 🔒 ZONA 2: PROTEÇÃO ABSOLUTA DA ROUPA E DECOTE
                    // Impede a IA de gerar seios expostos ou mudar o decote original
                    if (y > chinProtect)
                    {
                        // Protege todo o centro do peito/corpo
                        float tBody = (y - chinProtect) / (h - chinProtect);
                        float larguraProtecaoCentro = frx * (0.85f + tBody * 0.6f);

                        if (MathF.Abs(x - fx) < larguraProtecaoCentro)
                        {
                            row[x] = new Rgba32(0, 0, 0, 255);
                            continue;
                        }
                    }

                    if (y < hairTop || y > hairBottomMax)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // ── ZONA EDITÁVEL: CANAIS SOBRE OS OMBROS ─────────
                    // Abre espaço nas LATERAIS e SOBRE OS OMBROS para o cabelo cair pra frente
                    float distCenter = MathF.Abs(x - fx);
                    float minEditDist = frx * 0.85f; // Começa logo do lado do pescoço
                    float maxEditDist = frx * 3.2f;  // Vai até fora dos ombros (pega cabelo atrás)

                    bool naFaixaLateral = distCenter >= minEditDist && distCenter <= maxEditDist;

                    row[x] = naFaixaLateral
                        ? new Rgba32(255, 255, 255, 255)
                        : new Rgba32(0, 0, 0, 255);
                }
            }
        });

        // ── 3. Expansão para engolir cabelo antigo atrás dos ombros ──
        ExpandirParaCabeloAntigo(img, mask, fx, fy, frx, fry, chinY);

        // Dilatação moderada
        DilatarBranco(mask, 3);

        // Re-aplica a proteção do centro da roupa para a dilatação não invadir o decote
        ReplicarProtecaoDecote(mask, fx, frx, chinProtect, h);

        // Feather suave nas bordas da máscara
        mask.Mutate(c => c.GaussianBlur(6.0f));

        Directory.CreateDirectory(outputFolder);
        var maskPath = Path.Combine(outputFolder, $"mask_{modo}_{Guid.NewGuid()}.png");
        await mask.SaveAsPngAsync(maskPath, ct);

        return (maskPath, modo);
    }

    /// <summary>
    /// Detecta e adiciona à máscara qualquer cabelo escuro/antigo que esteja atrás dos ombros
    /// </summary>
    private static void ExpandirParaCabeloAntigo(
        Image<Rgba32> originalImg, Image<Rgba32> mask,
        float cx, float cy, float frx, float fry, float chinY)
    {
        int w = originalImg.Width;
        int h = originalImg.Height;

        mask.ProcessPixelRows(originalImg, (mAcc, iAcc) =>
        {
            for (int y = 0; y < h; y++)
            {
                var mRow = mAcc.GetRowSpan(y);
                var iRow = iAcc.GetRowSpan(y);

                for (int x = 0; x < w; x++)
                {
                    // Se for na região lateral/ombros e tiver cabelo escuro antigo
                    if (MathF.Abs(x - cx) > frx * 0.8f && MathF.Abs(x - cx) < frx * 3.5f)
                    {
                        if (EhCabeloAntigo(iRow[x]))
                        {
                            mRow[x] = new Rgba32(255, 255, 255, 255); // Marca para apagar/substituir
                        }
                    }
                }
            }
        });
    }

    private static void ReplicarProtecaoDecote(Image<Rgba32> mask, float cx, float frx, float chinProtect, int h)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = (int)chinProtect; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                float tBody = (y - chinProtect) / (h - chinProtect);
                float larguraProtecao = frx * (0.80f + tBody * 0.5f);

                for (int x = 0; x < row.Length; x++)
                {
                    if (MathF.Abs(x - cx) < larguraProtecao)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255); // Força PRETO no centro do peito
                    }
                }
            }
        });
    }

    private static bool EhCabeloAntigo(Rgba32 p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B));
        int min = Math.Min(p.R, Math.Min(p.G, p.B));
        bool escuro = max < 130;
        bool pele = p.R > 110 && p.G > 70 && p.B > 50 && (p.R - p.G) < 70 && p.R > p.B;
        return escuro && !pele;
    }

    private static void DilatarBranco(Image<Rgba32> mask, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            using var copy = mask.Clone();
            copy.ProcessPixelRows(mask, (srcAcc, dstAcc) =>
            {
                for (int y = 0; y < srcAcc.Height; y++)
                {
                    var src = srcAcc.GetRowSpan(y);
                    var dst = dstAcc.GetRowSpan(y);
                    var up = y > 0 ? srcAcc.GetRowSpan(y - 1) : src;
                    var dn = y < srcAcc.Height - 1 ? srcAcc.GetRowSpan(y + 1) : src;

                    for (int x = 0; x < src.Length; x++)
                    {
                        byte m = src[x].R;
                        if (x > 0) m = Math.Max(m, src[x - 1].R);
                        if (x < src.Length - 1) m = Math.Max(m, src[x + 1].R);
                        m = Math.Max(m, up[x].R);
                        m = Math.Max(m, dn[x].R);
                        if (m >= 128) dst[x] = new Rgba32(255, 255, 255, 255);
                    }
                }
            });
        }
    }

    private static int ExtrairCm(string? comprimento)
    {
        if (string.IsNullOrWhiteSpace(comprimento)) return 65;
        var match = Regex.Match(comprimento, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int cm)) return Math.Clamp(cm, 15, 120);
        return 65;
    }

    private static HairEditMode ResolverModo(int cm, bool cabeloAtualLongo)
    {
        if (cm <= 35) return cabeloAtualLongo ? HairEditMode.Shorten : HairEditMode.Recolor;
        return HairEditMode.Extend;
    }

    private static bool DetectarSeCabeloLongoAtual(Image<Rgba32> img, float fx, float chinY, float frx, float fry)
    {
        int startY = (int)chinY;
        int endY = Math.Min(img.Height - 1, (int)(chinY + fry * 2.5f));
        int startX = Math.Max(0, (int)(fx - frx * 2.5f));
        int endX = Math.Min(img.Width - 1, (int)(fx + frx * 2.5f));

        int total = 0, hair = 0;
        for (int y = startY; y < endY; y += 4)
            for (int x = startX; x < endX; x += 4)
            {
                total++;
                if (EhCabeloAntigo(img[x, y])) hair++;
            }
        return total > 0 && (float)hair / total > 0.05f;
    }
}