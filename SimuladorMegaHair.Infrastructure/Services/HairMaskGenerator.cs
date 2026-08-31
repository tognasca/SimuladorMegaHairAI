using SimuladorMegaHair.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
        int w = img.Width;
        int h = img.Height;

        // ── 1. Geometria Facial (Adaptável) ───────────────────
        // Assume rosto oval/feminino por default; funciona p/ homem também
        float fx, fy, frx, fry, faceTop, faceBottom, chinY;

        if (rostoDetectado != null)
        {
            fx = rostoDetectado.X + rostoDetectado.Width / 2f;
            fy = rostoDetectado.Y + rostoDetectado.Height / 2f;
            frx = Math.Max(1f, rostoDetectado.Width / 2f);
            fry = Math.Max(1f, rostoDetectado.Height / 2f);
            faceTop = rostoDetectado.Y;
            faceBottom = rostoDetectado.Y + rostoDetectado.Height;
            chinY = faceBottom; // base do queixo
        }
        else
        {
            // Default feminino: rosto ligeiramente mais alto e estreito
            fx = w * 0.50f;
            fy = h * 0.36f;  // um pouco mais alto que mask anterior (0.40)
            frx = w * 0.14f; // mais estreito que masculino (0.16)
            fry = h * 0.19f; // levemente maior verticalmente
            faceTop = fy - fry;
            faceBottom = fy + fry;
            chinY = faceBottom;
        }

        int cm = ExtrairCm(comprimentoDesejado);
        bool cabeloAtualLongo = DetectarSeCabeloLongoAtual(img, fx, faceBottom, frx, fry);
        var modo = ResolverModo(cm, cabeloAtualLongo);

        // ── 2. Extensão Vertical (Feminina) ──────────────────
        // Mulheres com mega hair de 85cm: cabelo chega à cintura/navel
        // Cabelo curto feminino (bob/pixie): pouco abaixo do queixo

        float quedaFator = cm switch
        {
            <= 25 => 0.6f,   // Pixie / super curto
            <= 35 => 1.0f,   // Bob / queixo
            <= 45 => 1.8f,   // Ombros (shoulder length)
            <= 55 => 2.6f,   // Clavícula / meio das costas
            <= 65 => 3.4f,   // Costas (bra strap)
            <= 75 => 4.4f,   // Cintura baixa
            _ => 5.5f    // Mega hair longo (85cm+) até cintura
        };

        // Topo: espaço suficiente para volume raiz/franja (importante p/ mulher)
        float hairTop = Math.Max(0, faceTop - fry * 0.65f);

        // Fundo: permitir queda até cintura (área de decolte e peito superior)
        float hairBottomMax = Math.Min(h - 1f, chinY + fry * quedaFator);

        // ── 3. Zonas de Proteção (Rosto + Acessórios + Roupa) ──

        // Proteção facial: leve expansão para incluir maquiagem/bochechas
        // Mas sem invadir a linha do cabelo (temples/raiz)
        float protectRx = frx * 0.95f;  // Um pouco mais largo que antes (maquiagem)
        float protectRy = fry * 1.08f;
        float chinProtect = chinY + fry * 0.20f;  // Queixo + início do pescoço

        // Zona protegida do decolte/peito (para preservar colar/blusa):
        // No feminino, o centro pode ser decote -> proteger menos que o masculino
        // ou proteger de forma inteligente (só se for tecido reconhecível)
        float chestProtectHalfW = frx * 0.38f;  // Mais estreito (decolte pode aparecer)
        float chestProtectTop = chinY + fry * 0.60f;  // Início do decolte/pescoço

        using var mask = new Image<Rgba32>(w, h, new Rgba32(0, 0, 0, 255));

        mask.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    // ── PROTEÇÃO DURO: ROSTO COMPLETO (inclui maquiagem) ──
                    float pdx = (x - fx) / protectRx;
                    float pdy = (y - fy) / protectRy;
                    bool noRosto = (pdx * pdx + pdy * pdy) <= 1.05f && y <= chinProtect;

                    if (noRosto)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // ── PROTEÇÃO: Centro do corpo (decote/estampa) ──
                    // Em mulheres, se houver decote V, o cabelo PODE cair nele
                    // Só bloqueamos o miolo absoluto para não gerar pele estranha
                    if (y > chestProtectTop && MathF.Abs(x - fx) < chestProtectHalfW)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // Fora dos limites verticais de cabelo
                    if (y < hairTop || y > hairBottomMax)
                    {
                        row[x] = new Rgba32(0, 0, 0, 255);
                        continue;
                    }

                    // ── CÁLCULO DO ENVELOPE DE CABELO (Forma Feminina) ──

                    float halfWidth = CalcularLarguraCabeloFeminino(
                        y, hairTop, faceTop, chinY, hairBottomMax,
                        frx, fry, cm);

                    bool dentro = MathF.Abs(x - fx) <= halfWidth;

                    // Lógica de "canal central" abaixo do queixo:
                    // Cabelo feminino cobre mais os lados (camadas)
                    // mas evita gerar cabelo no meio do decolte (estranho)
                    if (dentro && y > chinY + fry * 0.3f)
                    {
                        float tFall = (y - chinY) / Math.Max(1f, hairBottomMax - chinY);

                        // Canal central diminui gradualmente (forma de U aberta embaixo)
                        // Para megas longos, permite queda no meio tbm? Não, fica artificial
                        float gapCentral = frx * (0.35f + 0.15f * tFall); // 0.35 -> 0.50
                        if (MathF.Abs(x - fx) < gapCentral && cm > 45)
                            dentro = false; // Só laterais do peito, não centro
                    }

                    // Para cabelos MUITO CURTOS (pixie/bob): tudo acima do queixo é editável
                    // Abaixo do queixo: nada (ou muito pouco)
                    if (cm <= 30 && y > chinY + fry * 0.2f)
                        dentro = false;

                    row[x] = dentro
                        ? new Rgba32(255, 255, 255, 255)
                        : new Rgba32(0, 0, 0, 255);
                }
            }
        });

        // ── 4. Pós-processamento da Máscara ───────────────────

        // Dilatação: expande raiz e ombros para sombra natural
        int dilateIter = cm >= 55 ? 4 : (cm >= 35 ? 3 : 2);
        DilatarBranco(mask, dilateIter);

        // Redesenhar proteção DURA do rosto (dilatação não invade maquiagem)
        AplicarProtecaoFacialDura(mask, fx, fy, protectRx * 0.92f, protectRy * 0.92f, chinProtect);

        // Redesenhar proteção do corpo (opcional, mais leve aqui)
        AplicarProtecaoCorpo(mask, fx, chestProtectHalfW * 0.9f, chestProtectTop);

        // Feather: Borda suave para transição natural
        // Mulheres aceitam/transicionam bem feather entre 6-12px dependendo do estilo
        float sigma = cm switch
        {
            <= 30 => 4f,   // Curto: borda mais nítida (definição de corte)
            <= 50 => 7f,   // Médio: transição suave nas pontas
            _ => 10f   // Longo: transição muito suave (fios finos)
        };

        mask.Mutate(c => c.GaussianBlur(sigma));
        // ── 5. EXPANSÃO INTELIGENTE: Engolir o cabelo REAL que sobrou ──

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

        // Definir a "região de busca" de cabelo:
        // De 15% acima do topo da cabeça até 120% da queda desejada abaixo do queixo
        float topSearch = Math.Max(0, cy - fry * 2.0f);
        float bottomSearch = Math.Min(h - 1f, chinY + fry * (comprimentoCm > 50 ? 6.0f : 4.0f));

        // Largura da busca: bem larga para pegar cabelo caindo para os lados
        float halfWidth = frx * (comprimentoCm > 55 ? 3.8f : 2.8f);

        result.ProcessPixelRows(originalImg, (maskAcc, imgAcc) =>
        {
            for (int y = (int)topSearch; y <= (int)bottomSearch && y < h; y++)
            {
                var mRow = maskAcc.GetRowSpan(y);
                var iRow = imgAcc.GetRowSpan(y);

                for (int x = 0; x < w; x++)
                {
                    // Pula se JÁ É BRANCO (já vai ser editado)
                    if (mRow[x].R > 200) continue;

                    // Pula se estiver DENTRO do rosto protegido ( oval facial )
                    float pdx = (x - cx) / (frx * 0.95f);
                    float pdy = (y - cy) / (fry * 1.08f);
                    if ((pdx * pdx + pdy * pdy) <= 1.0f && y <= chinY + fry * 0.25f)
                        continue;

                    // Distância horizontal limite (não quer pintar o fundo da parede)
                    if (MathF.Abs(x - cx) > halfWidth) continue;

                    // VERIFICA SE É CABELO REAL
                    if (EhCabeloDetectavel(iRow[x]))
                    {
                        // Marca como BRANCO = editar
                        mRow[x] = new Rgba32(255, 255, 255, 255);
                    }
                }
            }
        });

        // Segunda passagem: dilatação moderada para unir pedaços soltos
        DilatarBranco(result, 2); // 2 iterações para fechar buracos

        return result;
    }

    /// <summary>
    /// Detecta pixel de cabelo (escuro, castanho, ou qualquer tom que não seja pele/fundo claro)
    /// </summary>
    private static bool EhCabeloDetectavel(Rgba32 p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B));
        int min = Math.Min(p.R, Math.Min(p.G, p.B));

        // Regra 1: Escuros óbvios (preto, castanho escuro)
        bool escuro = max < 140 && min < 100;

        // Regra 2: Castanhos médios (cabelo natural não pintado)
        bool castanho = max is > 80 and < 200 &&
                       (p.R > p.B * 0.9f) &&   // Não é azul
                       (Math.Abs(p.R - p.G) < 60); // Tons terrosos

        // Regra 3: Ruivo/Cobre
        bool ruivo = p.R > 150 && p.R > p.G * 1.2f && p.B < p.R * 0.6f;

        // Excluir: Pele clara (base rosa/bege claro)
        bool peleClara = max > 180 && min > 130 && p.R > p.B && (p.R - p.G) < 40;

        // Excluir: Fundo branco/cinza muito claro (parede)
        bool fundoClaro = max > 220 && min > 200;

        // Excluir: Roupa vermelha/laranja brilhante (confunde com ruivo, mas geralmente saturada)
        bool roupaSaturada = max > 200 && (max - min) > 120 &&
                            (p.R > 200 || p.G > 200 || p.B > 200);

        return (escuro || castanho || ruivo) && !peleClara && !fundoClaro && !roupaSaturada;
    }
    /// <summary>
    /// Calcula meia-largura do envelope de cabelo considerando forma feminina:
    /// - Topo: crown volume (topete/volume raiz)
    /// - Temples: face-framing (mechas na cara)
    /// - Laterais: camadas/volume
    /// - Ombros: queda em cortina (curtain)
    /// - Peito: duas laterais (long hair behavior)
    /// </summary>
    private static float CalcularLarguraCabeloFeminino(
        int y,
        float hairTop,
        float faceTop,
        float chinY,
        float hairBottom,
        float frx,
        float fry,
        int cm)
    {
        // Multiplicadores de largura por zona (adaptados para cabeleireiro feminino)
        float crownWidth = 1.10f;  // Raiz (mais justo que homem para volume controlado)
        float templeWidth = 1.65f;  // Temples (aberto para face-framing)
        float cheekWidth = 1.95f;  // Bochechas (maior volume lateral feminino)
        float jawWidth = 2.15f;  // Mandíbula/orelhas
        float neckWidth = 2.35f;  // Pescoço/ombros (máxima largura)
        float chestWidth = 1.85f;  // Peito lateral (reduz, cai em duas tiras)
        float bottomWidth = 1.55f;  // Pontas extremas (se chegar ao fundo)

        if (y <= faceTop)
        {
            // Zona 1: Couro / Coroa / Franja
            float t = (y - hairTop) / Math.Max(1f, faceTop - hairTop);
            t = Math.Clamp(t, 0f, 1f);
            return frx * (crownWidth + t * (templeWidth - crownWidth));
        }

        if (y <= chinY)
        {
            // Zona 2: Rosto todo (temples até max bochecha)
            float t = (y - faceTop) / Math.Max(1f, chinY - faceTop);
            return frx * (templeWidth + t * (jawWidth - templeWidth));
        }

        // Abaixo do queijo: ombros -> decolte -> peito
        float belowChin = y - chinY;
        float shoulderZone = fry * 2.0f;    // Até ombros
        float chestZone = fry * 4.0f;    // Até peito (ajustável por cm)

        if (belowChin <= shoulderZone)
        {
            // Zona 3: Pescoço/Ombros (queda em V larga)
            float t = belowChin / Math.Max(1f, shoulderZone);
            return frx * (jawWidth + t * (neckWidth - jawWidth));
        }

        if (belowChin <= chestZone || belowChin <= (hairBottom - chinY) * 0.6f)
        {
            // Zona 4: Peito superior / Decolte (começa a afunilar)
            float t = (belowChin - shoulderZone) / Math.Max(1f, chestZone - shoulderZone);
            t = Math.Clamp(t, 0f, 1f);
            return frx * (neckWidth + t * (chestWidth - neckWidth));
        }

        // Zona 5: Pontas longas (duas laterais finas)
        float tEnd = (belowChin - chestZone) / Math.Max(1f, (hairBottom - chinY) - chestZone);
        tEnd = Math.Clamp(tEnd, 0f, 1f);
        return frx * (chestWidth + tEnd * (bottomWidth - chestWidth));
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
                        if (m >= 128)
                            dst[x] = new Rgba32(255, 255, 255, 255);
                    }
                }
            });
        }
    }

    private static void AplicarProtecaoFacialDura(
        Image<Rgba32> mask,
        float cx, float cy,
        float rx, float ry,
        float maxY)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < (int)maxY && y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    if ((dx * dx + dy * dy) <= 1.0f)
                        row[x] = new Rgba32(0, 0, 0, 255);
                }
            }
        });
    }

    private static void AplicarProtecaoCorpo(
        Image<Rgba32> mask,
        float cx,
        float halfW,
        float topY)
    {
        mask.ProcessPixelRows(acc =>
        {
            for (int y = (int)topY; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (MathF.Abs(x - cx) < halfW)
                        row[x] = new Rgba32(0, 0, 0, 255);
                }
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

    /// <summary>
    /// Detector de pixels de cabelo adaptado para tons femininos
    /// (aceita loiros claros, ruivos, colorações fantasias)
    /// </summary>
    private static bool PareceCabeloFeminino(Rgba32 p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B));
        int min = Math.Min(p.R, Math.Min(p.G, p.B));
        int sat = max == 0 ? 0 : (max - min) * 255 / max;

        // Aceita mais faixas de cor que o detector masculino
        bool escuro = max < 115;
        bool medio = max >= 115 && max < 200 && sat > 25; // castanhos, loiros escuros
        bool claroColorido = max >= 200 && sat > 30;       // loiros platinados, rosas, azuis
        bool ruivo = p.R > 150 && p.G > 80 && p.B < 120 && (p.R - p.B) > 40;

        bool pele = p.R > 95 && p.G > 65 && p.B > 55 && p.R > p.B && (p.R - p.G) < 90 && sat < 35;

        return (escuro || medio || claroColorido || ruivo) && !pele;
    }
}