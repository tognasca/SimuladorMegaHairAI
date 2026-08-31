using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Models;
using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class HairMaskGenerator
{
    private const double AreaMaximaPct = 0.55; // trava de segurança

    public static async Task<(string maskPath, HairEditMode modoUtilizado)> GerarMascaraInteligenteAsync(
        string imagemOriginalPath,
        string outputFolder,
        FaceBox? rostoDetectado,
        string comprimentoDesejado,
        CancellationToken ct = default)
        => await GerarMascaraInteligenteAsync(
            imagemOriginalPath, outputFolder, rostoDetectado, comprimentoDesejado,
            http: null, repOpts: null, logger: null, ct);

    /// <summary>
    /// Overload com segmentação por IA real (Grounded SAM). Quando
    /// http/repOpts/logger são informados, a máscara passa por uma segunda
    /// camada de precisão: o resultado da IA é INTERSECTADO com a máscara
    /// geométrica (nunca substituído). Isso significa que a IA só pode
    /// REDUZIR/refinar a área editável — jamais expandi-la para fora do
    /// envelope geométrico seguro (que já protege rosto/fundo por
    /// construção). Defesa em profundidade: duas camadas independentes
    /// precisam concordar que um pixel é "cabelo" para ele ser editado.
    /// </summary>
    public static async Task<(string maskPath, HairEditMode modoUtilizado)> GerarMascaraInteligenteAsync(
        string imagemOriginalPath,
        string outputFolder,
        FaceBox? rostoDetectado,
        string comprimentoDesejado,
        HttpClient? http,
        SimuladorMegaHair.Infrastructure.Configuration.ReplicateOptions? repOpts,
        Microsoft.Extensions.Logging.ILogger? logger,
        CancellationToken ct = default)
    {
        using var img = await Image.LoadAsync<Rgba32>(imagemOriginalPath, ct);
        int w = img.Width, h = img.Height;

        // ── 1. Geometria relativa ao rosto ───────────────────
        float fx, fy, frx, fry, faceTop, chinY;
        if (rostoDetectado is not null)
        {
            frx = Math.Max(8f, rostoDetectado.Width / 2f);
            fry = Math.Max(8f, rostoDetectado.Height / 2f);
            fx = rostoDetectado.X + frx;
            fy = rostoDetectado.Y + fry;
            faceTop = rostoDetectado.Y;
            chinY = rostoDetectado.Y + rostoDetectado.Height;
        }
        else
        {
            fx = w * .5f; fy = h * .34f;
            frx = w * .13f; fry = h * .17f;
            faceTop = fy - fry; chinY = fy + fry;
        }

        int cm = ExtrairCm(comprimentoDesejado);

        // ── 2. Amostra a cor REAL do cabelo desta cliente ────
        var perfil = AmostrarCabelo(img, fx, faceTop, frx, fry);

        // ── 3. Mapa de candidatos a cabelo (sem cor fixa) ────
        var (rx0, ry0, rx1, ry1) = RegiaoInteresse(w, h, fx, fy, frx, fry, cm);
        byte[] hairMap = ClassificarCabelo(img, perfil, rx0, ry0, rx1, ry1);

        FecharBuracos(hairMap, w, h, rx0, ry0, rx1, ry1, raio: 2);

        // ── 4. Componente conectado a partir do couro cabeludo ─
        byte[] conectado = CrescerDoCouroCabeludo(
            hairMap, w, h, fx, faceTop, frx, fry, rx0, ry0, rx1, ry1);

        bool cabeloLongo = ProporcaoAbaixoDoQueixo(conectado, w, h, fx, chinY, frx, fry) > 0.05f;
        var modo = cm <= 35 && cabeloLongo ? HairEditMode.Shorten : HairEditMode.Extend;

        // ── 5. Monta a máscara ───────────────────────────────
        using var mask = new Image<Rgba32>(w, h, new Rgba32(0, 0, 0, 255));
        PintarMapa(mask, conectado);

        if (modo == HairEditMode.Extend)
            PintarEnvelopeQueda(mask, img, fx, fy, frx, fry, faceTop, chinY, cm);

        Dilatar(mask, cm >= 55 ? 4 : 3);

        // ── 6. Proteções (sempre por último) ─────────────────
        ProtegerRosto(mask, fx, fy, frx * .95f, fry * 1.06f, chinY + fry * .12f);
        ProtegerColoECentro(mask, img, fx, frx, chinY, fry);
        ForaDaROI(mask, rx0, ry0, rx1, ry1);

        // ── 7. Trava de área ─────────────────────────────────
        for (int i = 0; i < 6 && AreaBranca(mask) > AreaMaximaPct; i++)
            Erodir(mask);

        mask.Mutate(c => c.GaussianBlur(cm >= 55 ? 4.5f : 3.5f));

        // reassegura após blur
        ProtegerRosto(mask, fx, fy, frx * .90f, fry * 1.00f, chinY + fry * .08f);
        ProtegerCentroDuro(mask, fx, frx * .40f, chinY + fry * .10f, h);

        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, $"mask_{modo}_{Guid.NewGuid():N}.png");
        await mask.SaveAsPngAsync(path, ct);
        return (path, modo);
    }

    // ════════════════════════════════════════════════════════
    //  PERFIL DE COR DO CABELO (amostrado da foto)
    // ════════════════════════════════════════════════════════

    private readonly record struct HairProfile(
        float NR, float NG, float NB,   // cromaticidade normalizada
        float Lum,                      // luminância média
        bool Acromatico);               // preto/branco/cinza

    private static HairProfile AmostrarCabelo(
        Image<Rgba32> img, float fx, float faceTop, float frx, float fry)
    {
        int y0 = (int)Math.Max(0, faceTop - fry * .60f);
        int y1 = (int)Math.Max(1, faceTop - fry * .05f);
        int x0 = (int)Math.Max(0, fx - frx * .60f);
        int x1 = (int)Math.Min(img.Width - 1, fx + frx * .60f);

        double sr = 0, sg = 0, sb = 0, sl = 0;
        int n = 0;

        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var p = img[x, y];
                if (EhPele(p)) continue;
                float lum = Lum(p);
                if (lum > 240) continue;            // estouro / fundo branco
                sr += p.R; sg += p.G; sb += p.B; sl += lum; n++;
            }

        if (n < 30)
            return new HairProfile(.34f, .33f, .33f, 70f, true); // fallback escuro

        float r = (float)(sr / n), g = (float)(sg / n), b = (float)(sb / n);
        float soma = Math.Max(1f, r + g + b);
        float lumM = (float)(sl / n);

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        bool acro = max <= 0 ? true : (max - min) / max < 0.18f;

        return new HairProfile(r / soma, g / soma, b / soma, lumM, acro);
    }

    private static byte[] ClassificarCabelo(
        Image<Rgba32> img, HairProfile hp, int x0, int y0, int x1, int y1)
    {
        int w = img.Width, h = img.Height;
        var map = new byte[w * h];

        img.ProcessPixelRows(acc =>
        {
            for (int y = y0; y <= y1; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = x0; x <= x1; x++)
                {
                    var p = row[x];
                    if (EhPele(p)) continue;

                    float lum = Lum(p);
                    if (lum > 245) continue;                 // fundo estourado

                    // faixa de luminância ampla (cabelo tem brilho e sombra)
                    float loLum = hp.Lum * 0.25f - 25f;
                    float hiLum = hp.Lum + 85f;
                    if (lum < loLum || lum > hiLum) continue;

                    float soma = Math.Max(1, p.R + p.G + p.B);
                    float nr = p.R / soma, ng = p.G / soma, nb = p.B / soma;
                    float d = MathF.Abs(nr - hp.NR) + MathF.Abs(ng - hp.NG) + MathF.Abs(nb - hp.NB);

                    bool ok = hp.Acromatico
                        ? d < 0.14f                    // preto/cinza/branco: tolerância maior
                        : d < 0.10f;                   // loiro/ruivo/castanho: cromaticidade

                    if (ok) map[y * w + x] = 1;
                }
            }
        });

        return map;
    }

    // ════════════════════════════════════════════════════════
    //  COMPONENTE CONECTADO (mata ruído de fundo)
    // ════════════════════════════════════════════════════════

    private static byte[] CrescerDoCouroCabeludo(
        byte[] map, int w, int h,
        float fx, float faceTop, float frx, float fry,
        int x0, int y0, int x1, int y1)
    {
        var outMap = new byte[w * h];
        var fila = new Queue<int>();

        int sy0 = (int)Math.Max(0, faceTop - fry * .70f);
        int sy1 = (int)Math.Max(1, faceTop + fry * .10f);
        int sx0 = (int)Math.Max(0, fx - frx * .85f);
        int sx1 = (int)Math.Min(w - 1, fx + frx * .85f);

        for (int y = sy0; y < sy1; y++)
            for (int x = sx0; x < sx1; x++)
            {
                int i = y * w + x;
                if (map[i] == 1 && outMap[i] == 0)
                {
                    outMap[i] = 1;
                    fila.Enqueue(i);
                }
            }

        // se não achou semente (chapéu, careca, luz forte) devolve o mapa cru
        if (fila.Count == 0) return map;

        // Detecta onde está o cabelo ESCURO/ORIGINAL na foto
        // e pinta de BRANCO na máscara (para a IA recriar/recolore)
        var maskExpanded = ExpandirParaCabeloReal(img, mask, fx, fy, frx, fry, chinY, cm);

        // ── 6. SEGUNDA CAMADA: Segmentação por IA real (Grounded SAM) ──
        // Se disponível, intersecta o resultado da IA com o envelope
        // geométrico. A IA só pode REFINAR (reduzir) a área — nunca
        // expandi-la para fora do que a geometria já considera seguro.
        // Isso corrige o principal ponto fraco do método antigo (que era
        // 100% heurística de cor/geometria, sem nenhum modelo treinado).
        if (http is not null && repOpts is not null && logger is not null)
        {
            var iaMaskPath = await HairSegmentationService.SegmentarCabeloAsync(
                http, repOpts, imagemOriginalPath, outputFolder, logger, ct);

            if (iaMaskPath is not null)
            {
                try
                {
                    // ── Proteção para casos extremos: cliente careca, cabelo
                    // raspado, ou mudança drástica curto→longo. Nesses casos
                    // a IA de segmentação pode encontrar POUCO OU NENHUM
                    // cabelo na foto atual (porque quase não há cabelo pra
                    // detectar). Se intersectássemos cegamente, a máscara
                    // final zeraria e a simulação simplesmente não geraria
                    // cabelo nenhum — inaceitável para o negócio (o cabelo
                    // DESEJADO nunca depende do cabelo ATUAL).
                    //
                    // Regra: só aplicamos a interseção se a IA encontrou uma
                    // área plausível de cabelo (>= 8% da área do envelope
                    // geométrico). Abaixo disso, confiamos só na geometria,
                    // que é calculada a partir do COMPRIMENTO DESEJADO e da
                    // posição do rosto — não do cabelo atual — e por isso
                    // funciona igual para careca, raspado, curto→longo,
                    // longo→curto ou qualquer combinação.
                    var areaIa = await CalcularAreaBrancaAsync(iaMaskPath, ct);
                    var areaGeo = ContarPixelsBrancos(maskExpanded);
                    var proporcao = areaGeo > 0 ? (float)areaIa / areaGeo : 0f;

                    if (proporcao >= 0.08f)
                    {
                        await IntersectarComMascaraIaAsync(maskExpanded, iaMaskPath, ct);
                        logger.LogInformation(
                            "[MASK] Máscara refinada por IA real (Grounded SAM) aplicada ({P:P0} de cobertura).",
                            proporcao);
                    }
                    else
                    {
                        logger.LogInformation(
                            "[MASK] IA encontrou pouco/nenhum cabelo na foto atual ({P:P0} — provável careca, " +
                            "raspado ou mudança drástica de comprimento). Mantendo só a máscara geométrica, " +
                            "calculada a partir do comprimento DESEJADO, não do cabelo atual.",
                            proporcao);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[MASK] Falha ao combinar máscara de IA; seguindo só com geométrica.");
                }
                finally
                {
                    try { if (File.Exists(iaMaskPath)) File.Delete(iaMaskPath); } catch { /* ignore */ }
                }
            }
        }

        Directory.CreateDirectory(outputFolder);
        // Salva a versão expandida
        var maskPath = Path.Combine(outputFolder, $"mask_{modo}_{Guid.NewGuid()}.png");
        await maskExpanded.SaveAsPngAsync(maskPath, ct);

        return (maskPath, modo);
    }

    /// <summary>
    /// Conta pixels "brancos" (editáveis) numa máscara já carregada em memória.
    /// </summary>
    private static long ContarPixelsBrancos(Image<Rgba32> mask)
    {
        long total = 0;
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (row[x].R > 127) total++;
            }
        });
        return total;
    }

    /// <summary>
    /// Conta pixels "brancos" (editáveis) numa máscara salva em disco (a
    /// que veio da segmentação por IA).
    /// </summary>
    private static async Task<long> CalcularAreaBrancaAsync(string maskPath, CancellationToken ct)
    {
        using var mask = await Image.LoadAsync<L8>(maskPath, ct);
        long total = 0;
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (row[x].PackedValue > 127) total++;
            }
        });
        return total;
    }

    /// <summary>
    /// Intersecta (E lógico) a máscara geométrica com a máscara vinda da
    /// segmentação por IA. Um pixel só continua branco (editável) se AMBAS
    /// concordarem. Isso garante que a IA nunca consiga expandir a edição
    /// para fora da região já considerada segura pela geometria.
    /// </summary>
    private static async Task IntersectarComMascaraIaAsync(
        Image<Rgba32> maskGeometrica, string iaMaskPath, CancellationToken ct)
    {
        using var iaMask = await Image.LoadAsync<L8>(iaMaskPath, ct);

        if (iaMask.Size != maskGeometrica.Size)
            iaMask.Mutate(x => x.Resize(maskGeometrica.Size));

        maskGeometrica.ProcessPixelRows(iaMask, (geoAcc, iaAcc) =>
        {
            for (int y = 0; y < geoAcc.Height; y++)
            {
                var geoRow = geoAcc.GetRowSpan(y);
                var iaRow = iaAcc.GetRowSpan(y);

                for (int x = 0; x < geoRow.Length; x++)
                {
                    bool geoBranco = geoRow[x].R > 127;
                    bool iaBranco = iaRow[x].PackedValue > 127;

                    geoRow[x] = (geoBranco && iaBranco)
                        ? new Rgba32(255, 255, 255, 255)
                        : new Rgba32(0, 0, 0, 255);
                }
            }
        });
    }
    /// <summary>
    /// Expande a máscara branca para cobrir PIXELS DE CABELO REAL que ficaram de fora
    /// da zona geométrica. Resolve o problema de "meio preto meio loiro".
    /// </summary>
    private static Image<Rgba32> ExpandirParaCabeloReal(
        Image<Rgba32> originalImg,
        Image<Rgba32> maskGeometrica,
        float cx, float cy,
        float frx, float fry,
        float chinY,
        int comprimentoCm)
    {
        var result = maskGeometrica.Clone();
        int w = originalImg.Width;
        int h = originalImg.Height;

            for (int k = 0; k < 8; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < x0 || nx > x1 || ny < y0 || ny > y1) continue;
                int ni = ny * w + nx;
                if (map[ni] == 1 && outMap[ni] == 0)
                {
                    outMap[ni] = 1;
                    fila.Enqueue(ni);
                }
            }
        }

        return outMap;
    }

    // ════════════════════════════════════════════════════════
    //  ENVELOPE DE QUEDA (onde o mega hair NOVO vai existir)
    // ════════════════════════════════════════════════════════

    private static void PintarEnvelopeQueda(
        Image<Rgba32> mask, Image<Rgba32> img,
        float fx, float fy, float frx, float fry,
        float faceTop, float chinY, int cm)
    {
        float queda = cm switch
        {
            <= 35 => 1.2f,
            <= 55 => 2.9f,
            <= 75 => 4.2f,
            _ => 5.4f
        };

        float topo = Math.Max(0, faceTop - fry * .75f);
        float fundo = Math.Min(mask.Height - 1, chinY + fry * queda);
        float larguraMax = frx * (cm >= 55 ? 2.9f : 2.4f);
        float canalCentral = frx * .55f;

        mask.ProcessPixelRows(img, (mAcc, iAcc) =>
        {
            for (int y = (int)topo; y <= (int)fundo; y++)
            {
                var m = mAcc.GetRowSpan(y);
                var p = iAcc.GetRowSpan(y);

                for (int x = 0; x < m.Length; x++)
                {
                    float dx = MathF.Abs(x - fx);

                    if (y <= chinY)
                    {
                        float t = Math.Clamp((y - topo) / Math.Max(1f, chinY - topo), 0, 1);
                        float half = frx * (1.15f + MathF.Sin(t * MathF.PI * .5f) * 1.15f);
                        if (dx <= half) m[x] = White;
                    }
                    else
                    {
                        if (dx < canalCentral) continue;          // colo/roupa central
                        if (dx > larguraMax) continue;
                        if (EhPele(p[x]) && dx < frx * 1.15f) continue; // não inventa decote
                        m[x] = White;
                    }
                }
            }
        });
    }

    // ════════════════════════════════════════════════════════
    //  PROTEÇÕES
    // ════════════════════════════════════════════════════════

    private static void ProtegerRosto(
        Image<Rgba32> mask, float cx, float cy, float rx, float ry, float maxY)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height && y <= maxY; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    if (dx * dx + dy * dy <= 1.02f) row[x] = Black;
                }
            }
        });
    }

    /// <summary>Colo/decote: protege PELE no centro do torso (qualquer tom).</summary>
    private static void ProtegerColoECentro(
        Image<Rgba32> mask, Image<Rgba32> img,
        float fx, float frx, float chinY, float fry)
    {
        int h = mask.Height;
        float top = chinY + fry * .05f;

        mask.ProcessPixelRows(img, (mAcc, iAcc) =>
        {
            for (int y = (int)top; y < h; y++)
            {
                var m = mAcc.GetRowSpan(y);
                var p = iAcc.GetRowSpan(y);
                for (int x = 0; x < m.Length; x++)
                {
                    float dx = MathF.Abs(x - fx);
                    if (dx < frx * .42f) { m[x] = Black; continue; }        // coluna central
                    if (dx < frx * 1.25f && EhPele(p[x])) m[x] = Black;      // colo
                }
            }
        });
    }

    private static void ProtegerCentroDuro(Image<Rgba32> mask, float fx, float half, float top, int h)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = (int)top; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (MathF.Abs(x - fx) < half) row[x] = Black;
            }
        });
    }

    private static void ForaDaROI(Image<Rgba32> mask, int x0, int y0, int x1, int y1)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                bool foraY = y < y0 || y > y1;
                for (int x = 0; x < row.Length; x++)
                    if (foraY || x < x0 || x > x1) row[x] = Black;
            }
        });
    }

    // ════════════════════════════════════════════════════════
    //  UTILITÁRIOS
    // ════════════════════════════════════════════════════════

    private static readonly Rgba32 White = new(255, 255, 255, 255);
    private static readonly Rgba32 Black = new(0, 0, 0, 255);

    private static (int, int, int, int) RegiaoInteresse(
        int w, int h, float fx, float fy, float frx, float fry, int cm)
    {
        float lado = frx * (cm >= 55 ? 3.4f : 2.9f);
        float cima = fry * 2.1f;
        float baixo = fry * (cm switch { <= 35 => 2.0f, <= 55 => 4.0f, <= 75 => 5.4f, _ => 6.6f });

        int x0 = (int)Math.Clamp(fx - lado, 0, w - 1);
        int x1 = (int)Math.Clamp(fx + lado, 0, w - 1);
        int y0 = (int)Math.Clamp(fy - cima, 0, h - 1);
        int y1 = (int)Math.Clamp(fy + baixo, 0, h - 1);
        return (x0, y0, x1, y1);
    }

    private static float Lum(Rgba32 p) => 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;

    /// <summary>Detector de pele genérico (YCbCr) — funciona em qualquer tom.</summary>
    private static bool EhPele(Rgba32 p)
    {
        float y = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
        float cb = 128f - 0.168736f * p.R - 0.331264f * p.G + 0.5f * p.B;
        float cr = 128f + 0.5f * p.R - 0.418688f * p.G - 0.081312f * p.B;
        return y > 45f && cb >= 82f && cb <= 132f && cr >= 132f && cr <= 180f;
    }

    private static void PintarMapa(Image<Rgba32> mask, byte[] map)
    {
        int w = mask.Width;
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                int off = y * w;
                for (int x = 0; x < row.Length; x++)
                    if (map[off + x] == 1) row[x] = White;
            }
        });
    }

    private static void FecharBuracos(byte[] map, int w, int h, int x0, int y0, int x1, int y1, int raio)
    {
        var tmp = (byte[])map.Clone();
        for (int y = y0 + raio; y <= y1 - raio; y++)
            for (int x = x0 + raio; x <= x1 - raio; x++)
            {
                if (tmp[y * w + x] == 1) continue;
                int viz = 0;
                for (int dy = -raio; dy <= raio; dy++)
                    for (int dx = -raio; dx <= raio; dx++)
                        if (tmp[(y + dy) * w + (x + dx)] == 1) viz++;
                if (viz >= (raio * 2 + 1) * (raio * 2 + 1) / 2) map[y * w + x] = 1;
            }
    }

    private static float ProporcaoAbaixoDoQueixo(
        byte[] map, int w, int h, float fx, float chinY, float frx, float fry)
    {
        int y0 = (int)Math.Clamp(chinY, 0, h - 1);
        int y1 = (int)Math.Clamp(chinY + fry * 2.5f, 0, h - 1);
        int x0 = (int)Math.Clamp(fx - frx * 2.5f, 0, w - 1);
        int x1 = (int)Math.Clamp(fx + frx * 2.5f, 0, w - 1);

        int tot = 0, hit = 0;
        for (int y = y0; y < y1; y += 2)
            for (int x = x0; x < x1; x += 2)
            { tot++; if (map[y * w + x] == 1) hit++; }

        return tot == 0 ? 0 : (float)hit / tot;
    }

    private static void Dilatar(Image<Rgba32> mask, int it)
    {
        for (int i = 0; i < it; i++)
        {
            using var copy = mask.Clone();
            copy.ProcessPixelRows(mask, (s, d) =>
            {
                for (int y = 0; y < s.Height; y++)
                {
                    var sr = s.GetRowSpan(y);
                    var dr = d.GetRowSpan(y);
                    var up = y > 0 ? s.GetRowSpan(y - 1) : sr;
                    var dn = y < s.Height - 1 ? s.GetRowSpan(y + 1) : sr;
                    for (int x = 0; x < sr.Length; x++)
                    {
                        byte m = sr[x].R;
                        if (x > 0) m = Math.Max(m, sr[x - 1].R);
                        if (x < sr.Length - 1) m = Math.Max(m, sr[x + 1].R);
                        m = Math.Max(m, up[x].R);
                        m = Math.Max(m, dn[x].R);
                        if (m >= 128) dr[x] = White;
                    }
                }
            });
        }
    }

    private static void Erodir(Image<Rgba32> mask)
    {
        using var copy = mask.Clone();
        copy.ProcessPixelRows(mask, (s, d) =>
        {
            for (int y = 0; y < s.Height; y++)
            {
                var sr = s.GetRowSpan(y);
                var dr = d.GetRowSpan(y);
                var up = y > 0 ? s.GetRowSpan(y - 1) : sr;
                var dn = y < s.Height - 1 ? s.GetRowSpan(y + 1) : sr;
                for (int x = 0; x < sr.Length; x++)
                {
                    byte m = sr[x].R;
                    if (x > 0) m = Math.Min(m, sr[x - 1].R);
                    if (x < sr.Length - 1) m = Math.Min(m, sr[x + 1].R);
                    m = Math.Min(m, up[x].R);
                    m = Math.Min(m, dn[x].R);
                    if (m < 128) dr[x] = Black;
                }
            }
        });
    }

    private static double AreaBranca(Image<Rgba32> mask)
    {
        long brancos = 0, total = (long)mask.Width * mask.Height;
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    if (row[x].R > 127) brancos++;
            }
        });
    }

    private static int ExtrairCm(string? comprimento)
    {
        if (string.IsNullOrWhiteSpace(comprimento)) return 55; // Default médio feminino

        var match = Regex.Match(comprimento, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int cm))
            return Math.Clamp(cm, 15, 120);

        // ── Fallback por PALAVRA (bug real corrigido) ──────────────
        // O app MAUI envia rótulos em texto ("Curto"/"Médio"/"Longo"/
        // "Extra Longo"), sem número — antes disso, TODO texto sem
        // dígito caía no default fixo de 55cm, inclusive "Extra Longo",
        // o que fazia a máscara nunca se estender o suficiente para
        // mega hair de verdade. Mapeamento alinhado com as mesmas
        // faixas usadas em TraduzirComprimentoFeminino/ResolverModo.
        var texto = comprimento.ToLowerInvariant();
        if (texto.Contains("extra") && texto.Contains("longo")) return 85;
        if (texto.Contains("longo")) return 65;
        if (texto.Contains("medio") || texto.Contains("médio")) return 45;
        if (texto.Contains("curto")) return 30;

        return 55;
    }

    private static HairEditMode ResolverModo(int cm, bool cabeloAtualLongo)
    {
        // Lógica alinhada ao mercado de mega hair feminino
        if (cm <= 25)
            return cabeloAtualLongo ? HairEditMode.Shorten : HairEditMode.Recolor; // Pixie/Bob
        if (cm <= 45)
            return cabeloAtualLongo ? HairEditMode.Shorten : HairEditMode.Extend;    // Shoulder
        return HairEditMode.Extend; // Longo/Mega é sempre extend
    }

    private static bool DetectarSeCabeloLongoAtual(
        Image<Rgba32> img, float fx, float faceBottom, float frx, float fry)
    {
        // Amostra região abaixo do queixo e laterais
        int startY = (int)(faceBottom - fry * 0.5f); // inclui parte das bochechas
        int endY = Math.Min(img.Height - 1, (int)(faceBottom + fry * 3.0f)); // mais profundo p/ mulher
        int startX = Math.Max(0, (int)(fx - frx * 2.5f));
        int endX = Math.Min(img.Width - 1, (int)(fx + frx * 2.5f));

        int total = 0, hair = 0;
        for (int y = startY; y < endY; y += 3) // passo menor para melhor detecção
            for (int x = startX; x < endX; x += 3)
            {
                total++;
                if (PareceCabeloFeminino(img[x, y])) hair++;
            }
        return total > 0 && (float)hair / total > 0.06f; // threshold levemente menor
    }

    private static int ExtrairCm(string? c)
    {
        if (string.IsNullOrWhiteSpace(c)) return 55;
        var m = Regex.Match(c, @"\d+");
        return m.Success && int.TryParse(m.Value, out int cm) ? Math.Clamp(cm, 15, 120) : 55;
    }
}