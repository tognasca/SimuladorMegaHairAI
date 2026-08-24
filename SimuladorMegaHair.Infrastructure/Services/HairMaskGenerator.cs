using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Models;

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

        // 1. Define o Rosto
        float fx, fy, frx, fry, faceTop, faceBottom;
        if (rostoDetectado != null)
        {
            fx = rostoDetectado.X + rostoDetectado.Width / 2f;
            fy = rostoDetectado.Y + rostoDetectado.Height / 2f;
            frx = rostoDetectado.Width / 2f;
            fry = rostoDetectado.Height / 2f;
            faceTop = rostoDetectado.Y;
            faceBottom = rostoDetectado.Y + rostoDetectado.Height;
        }
        else
        {
            fx = w * 0.5f; fy = h * 0.40f;
            frx = w * 0.16f; fry = h * 0.20f;
            faceTop = fy - fry; faceBottom = fy + fry;
        }

        // 2. Detecta se a pessoa atualmente JÁ TEM cabelo longo
        bool cabeloAtualLongo = DetectarSeCabeloLongoAtual(img, fx, faceBottom, frx, fry);

        // 3. Resolve o Modo de Edição
        var modo = ResolverModo(comprimentoDesejado, cabeloAtualLongo);

        // 4. Cria a máscara inicial (PRETO = preservar, BRANCO = editar)
        using var mask = new Image<Rgba32>(w, h, new Rgba32(0, 0, 0, 255));

        // Zonas de Proteção do Rosto (NUNCA editar olhos, nariz, boca, barba)
        float protectRx = frx * 1.08f;
        float protectRy = fry * 1.15f;
        float chinProtect = faceBottom + fry * 0.30f; // Protege queixo e barba

        // Configuração de Altura conforme o modo
        float hairTop = Math.Max(0, faceTop - fry * 1.8f);
        float hairBottomMax = modo switch
        {
            HairEditMode.Extend => Math.Min(h - 1, faceBottom + fry * 5.0f),  // Alongar muito
            HairEditMode.Shorten => Math.Min(h - 1, faceBottom + fry * 4.5f), // Cobrir todo longo
            _ => Math.Min(h - 1, faceBottom + fry * 3.5f)                      // Recolorir existente
        };

        float hairRx = frx * (modo == HairEditMode.Extend ? 3.0f : 2.5f);

        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < w; x++)
                {
                    // --- A. Checa se está no ROSTO/BARBA (NÃO PODE EDITAR) ---
                    float pdx = (x - fx) / protectRx;
                    float pdy = (y - fy) / protectRy;
                    bool noRosto = (pdx * pdx + pdy * pdy) <= 1.0f && y <= chinProtect;

                    if (noRosto)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // --- B. Checa se é pixel de cabelo no topo/laterais do crânio ---
                    bool noCranio = y >= hairTop && y <= faceBottom + fry * 0.5f &&
                                    MathF.Abs(x - fx) <= hairRx;

                    // --- C. Checa se é mecha de cabelo longo nos ombros/peito ---
                    // CORRIGIDO AQUI: leitura direta com img[x, y]
                    bool mechaOmbro = y > faceBottom && y <= hairBottomMax &&
                                      PareceCabelo(img[x, y]);

                    // --- D. Zona de Crescimento (Espaço Vazio para Mega Hair) ---
                    bool zonaMegaHair = modo == HairEditMode.Extend &&
                                        y > faceBottom && y <= hairBottomMax &&
                                        MathF.Abs(x - fx) <= hairRx &&
                                        !EPeitoCentro(x, y, fx, faceBottom, frx, fry);

                    // --- LÓGICA FINAL POR MODO ---
                    bool editar = modo switch
                    {
                        HairEditMode.Shorten => noCranio || mechaOmbro, // COBRE TUDO para apagar
                        HairEditMode.Recolor => noCranio || mechaOmbro, // COBRE TUDO existente
                        HairEditMode.Extend => noCranio || mechaOmbro || zonaMegaHair, // Existente + Novo Espaço
                        _ => noCranio
                    };

                    row[x] = editar ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 255);
                }
            }
        });

        // 5. Suavização leve das bordas (Feather)
        mask.Mutate(c => c.GaussianBlur(2.0f));

        Directory.CreateDirectory(outputFolder);
        var maskPath = Path.Combine(outputFolder, $"mask_{modo}_{Guid.NewGuid()}.png");
        await mask.SaveAsPngAsync(maskPath, ct);

        return (maskPath, modo);
    }

    private static HairEditMode ResolverModo(string comprimentoDesejado, bool cabeloAtualLongo)
    {
        var comp = comprimentoDesejado?.Trim().ToLowerInvariant() ?? "";
        bool querCurto = comp is "curto" or "short" or "medio" or "médio" or "medium";
        bool querLongo = comp.Contains("longo") || comp.Contains("long");

        if (cabeloAtualLongo && querCurto)
            return HairEditMode.Shorten;

        if (querLongo)
            return HairEditMode.Extend;

        return HairEditMode.Recolor;
    }

    private static bool DetectarSeCabeloLongoAtual(
        Image<Rgba32> img, float fx, float faceBottom, float frx, float fry)
    {
        int startY = (int)faceBottom;
        int endY = Math.Min(img.Height - 1, (int)(faceBottom + fry * 2.5f));
        int startX = Math.Max(0, (int)(fx - frx * 2.2f));
        int endX = Math.Min(img.Width - 1, (int)(fx + frx * 2.2f));

        int totalAmostras = 0;
        int pixelsCabelo = 0;

        for (int y = startY; y < endY; y += 4)
        {
            for (int x = startX; x < endX; x += 4)
            {
                totalAmostras++;
                if (PareceCabelo(img[x, y]))
                    pixelsCabelo++;
            }
        }

        if (totalAmostras == 0) return false;

        float taxaCabelo = (float)pixelsCabelo / totalAmostras;
        return taxaCabelo > 0.07f;
    }

    private static bool PareceCabelo(Rgba32 p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B));
        int min = Math.Min(p.R, Math.Min(p.G, p.B));
        int sat = max == 0 ? 0 : (max - min) * 255 / max;

        bool escuro = max < 110;
        bool colorido = sat > 35 && max < 210;

        bool eTomPele = p.R > 80 && p.G > 50 && p.B > 35 &&
                        p.R > p.B && (p.R - p.G) < 80 && Math.Abs(p.R - p.G) > 8;

        return (escuro || colorido) && !eTomPele;
    }

    private static bool EPeitoCentro(float x, float y, float fx, float faceBottom, float frx, float fry)
    {
        return y > faceBottom + fry * 2.0f && MathF.Abs(x - fx) < frx * 0.7f;
    }
}