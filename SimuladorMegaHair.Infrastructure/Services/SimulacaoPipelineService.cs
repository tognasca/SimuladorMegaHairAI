using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
using SimuladorMegaHair.Infrastructure.Configuration;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class SimulacaoPipelineService : IImageSimulationService
{
    private readonly HttpClient _http;
    private readonly ReplicateOptions _rep;
    private readonly OpenAIOptions _oai;
    private readonly SimulacaoOptions _sim;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SimulacaoPipelineService> _logger;
    private readonly HairZoneGenerator _zona;
    private readonly HairSegmenter _segmenter;
    private readonly OpenAiHairClient _openAi;
    private readonly Dictionary<string, string> _b64Cache = new();

    private const string ReplicateBaseUrl = "https://api.replicate.com/v1";

    // Modelo de edição por instrução. Se o resultado cru ainda vier ruim, teste "black-forest-labs/flux-kontext-max".
    private const string KontextModel = "black-forest-labs/flux-kontext-pro";

    public SimulacaoPipelineService(
        HttpClient http,
        IOptions<ReplicateOptions> rep,
        IOptions<OpenAIOptions> oai,
        IOptions<SimulacaoOptions> sim,
        IWebHostEnvironment env,
        ILogger<SimulacaoPipelineService> logger,
        HairZoneGenerator zona,
        HairSegmenter segmenter,
        OpenAiHairClient openAi)
    {
        _http = http;
        _rep = rep.Value;
        _oai = oai.Value;
        _sim = sim.Value;
        _env = env;
        _logger = logger;
        _zona = zona;
        _segmenter = segmenter;
        _openAi = openAi;
    }

    // ═══════════════════════════════════════════════════════════
    //  (legado) mantido por compatibilidade com a interface
    // ═══════════════════════════════════════════════════════════
    public async Task<(string url, string? aviso)> PipelineKontextAsync(
        string imagemPath, SimulacaoRequest req, CancellationToken ct)
    {
        var prompt = KontextPrompt.Build(req.Comprimento, req.Cor, req.TipoCabelo, req.MetodoMegaHair, HairEditMode.Extend);

        var body = new
        {
            input = new
            {
                prompt,
                input_image = ConverterBase64(imagemPath),
                aspect_ratio = "match_input_image",
                output_format = "png",
                safety_tolerance = 2
            }
        };

        var resultado = await ExecutarComRetryRateLimitAsync(body, ct, KontextModel);
        return (resultado, null);
    }

    // ═══════════════════════════════════════════════════════════
    //  ENTRADA
    // ═══════════════════════════════════════════════════════════
    public async Task<SimulacaoResult> GerarSimulacaoAsync(
        SimulacaoRequest req,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _b64Cache.Clear();
        string? imagemPrep = null;
        string? maskPath = null;
        string? zonaAjustadaPath = null;
        string? guiaPath = null;

        _logger.LogInformation("═══ SIMULAÇÃO [{Provider}] ═══", req.Provider);

        try
        {
            // 1. Resolve e prepara imagem
            var imagemAbs = ResolverCaminho(req.ImagemOriginalPath);
            var tempFolder = GarantirPasta("wwwroot", "temp");
            imagemPrep = await ImagePreparer.PrepararParaOpenAiAsync(
                imagemAbs, tempFolder, _rep.ImageSize);

            // 2. Rosto (obrigatório)
            var rosto = DetectarRostoSeguro(imagemPrep)
                ?? throw new InvalidOperationException(
                    "Não encontrei um rosto na foto. Envie uma foto frontal e bem iluminada.");

            bool foraDoQuadro = false;
            float yAlvoPx = 0f;
            Rectangle faceRect = default;
            string avisoQuadro = "";

            // Rosto pequeno demais = foto de corpo inteiro. O modelo re-renderiza a imagem e perde o alinhamento.
            {
                var infoPrep = Image.Identify(imagemPrep);
                {
                    float rx = (float)rosto.X, ry = (float)rosto.Y, rw = (float)rosto.Width, rh = (float)rosto.Height;
                    if (rw <= 1.5f && rh <= 1.5f) { rx *= infoPrep.Width; rw *= infoPrep.Width; ry *= infoPrep.Height; rh *= infoPrep.Height; }
                    faceRect = new Rectangle((int)rx, (int)ry, (int)rw, (int)rh);
                }
                float fh = (float)rosto.Height;
                float fracRosto = fh <= 1.5f ? fh : fh / infoPrep.Height;
                _logger.LogInformation("[FOTO] rosto ocupa {P:P1} da altura da imagem", fracRosto);
                if (fracRosto < 0.11f)
                    throw new InvalidOperationException(
                        "O rosto está muito pequeno na foto. Aproxime-se do celular e enquadre da cabeça até a linha indicada no guia de foto " +
                        "(rosto ocupando pelo menos 1/8 da altura da foto).");

                // Onde o cabelo do comprimento pedido terminaria na foto (cabeça adulta ≈ 23 cm).
                float faceHpx = fh <= 1.5f ? fh * infoPrep.Height : fh;
                float faceYpx = (float)rosto.Y; if (faceYpx <= 1.5f) faceYpx *= infoPrep.Height;
                float chinPx = faceYpx + faceHpx;
                float headHpx = faceHpx * 1.45f;
                float topPx = chinPx - headHpx;
                var mcm = System.Text.RegularExpressions.Regex.Match(req.Comprimento ?? "", @"\d+");
                int cmReq = mcm.Success && int.TryParse(mcm.Value, out var vcm) ? vcm : 60;
                float yAlvo = topPx + cmReq * headHpx / 23f;
                yAlvoPx = yAlvo;
                foraDoQuadro = req.Modo == HairEditMode.Extend && yAlvo > infoPrep.Height;

                // Espaço acima da cabeça (o Kontext ainda corta ~2% do topo da imagem).
                float espacoAcima = topPx / headHpx;
                _logger.LogInformation("[FOTO] espaco acima da cabeca = {E:P0} da altura da cabeca", espacoAcima);
                if (espacoAcima < 0.15f)
                    avisoQuadro += (avisoQuadro.Length > 0 ? " " : "") +
                        "Deixe mais espaço acima da cabeça (pelo menos um palmo) para o volume do cabelo caber na foto.";
                _logger.LogInformation("[FOTO] fim do cabelo estimado em y={Y:F0} de {H}px (fora do quadro: {F})",
                    yAlvo, infoPrep.Height, foraDoQuadro);
                if (foraDoQuadro)
                    avisoQuadro += (avisoQuadro.Length > 0 ? " " : "") + "O comprimento escolhido passa da borda inferior da foto, então o cabelo aparece só até onde a foto alcança. " +
                                  "Para ver o comprimento completo, envie uma foto enquadrada até a linha indicada no guia de foto.";
            }

            // 3. Zona onde a edição é permitida (usada só no composite final)
            var masksFolder = GarantirPasta("wwwroot", "masks");
            var modoEdit = req.Modo;
            // AJUSTE: use os campos reais do seu FaceBox (X, Y, Width, Height).
            maskPath = await _zona.GerarAsync(
                imagemPrep, masksFolder,
                rosto.X, rosto.Y, rosto.Width, rosto.Height,
                req.Comprimento, modoEdit, ct);

            zonaAjustadaPath = await _zona.GerarAsync(
                imagemPrep, masksFolder,
                rosto.X, rosto.Y, rosto.Width, rosto.Height,
                req.Comprimento, modoEdit, ct, ajustada: true, tipoCabelo: req.TipoCabelo);

            // Métodos guiados pela máscara do comprimento exato (ver MEGAHAIR_METODO). Padrão: kontext (texto).
            var metodoSel = (Environment.GetEnvironmentVariable("MEGAHAIR_METODO") ?? "kontext").Trim().ToLowerInvariant();
            if (modoEdit == HairEditMode.Extend && (metodoSel == "flux2-guia" || metodoSel == "fill-mascara"))
            {
                var mg = System.Text.RegularExpressions.Regex.Match(req.Comprimento ?? "", @"\d+");
                int cmG = mg.Success && int.TryParse(mg.Value, out var vg) ? vg : 60;
                guiaPath = await _zona.GerarAsync(
                    imagemPrep, masksFolder,
                    rosto.X, rosto.Y, rosto.Width, rosto.Height,
                    req.Comprimento, modoEdit, ct, ajustada: true, tipoCabelo: req.TipoCabelo, cmExato: cmG);
            }

            var auditOverlay = await HairMaskAudit.SalvarAsync(
                imagemPrep, maskPath,
                GarantirPasta("wwwroot", "masks", "audit"),
                _logger, ct);

            // 4. Pipeline — OpenAI gpt-image-1 (tipo ChatGPT) é o caminho recomendado.
            var metodoEnv = (Environment.GetEnvironmentVariable("MEGAHAIR_METODO") ?? "").Trim().ToLowerInvariant();
            var providerEfetivo = metodoEnv is "openai" or "gpt" or "chatgpt"
                ? ImageProvider.OpenAI
                : req.Provider;

            string resultadoUrl;
            string? aviso;

            if (providerEfetivo == ImageProvider.OpenAI)
            {
                (resultadoUrl, aviso) = await PipelineOpenAiAsync(imagemPrep, req, faceRect, ct);
            }
            else if (providerEfetivo == ImageProvider.Replicate)
            {
                (resultadoUrl, aviso) = await PipelineKontextHairAsync(
                    imagemPrep, maskPath, zonaAjustadaPath, guiaPath, req, modoEdit, foraDoQuadro, yAlvoPx, faceRect, ct);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Provider '{req.Provider}' não implementado. Use OpenAI (recomendado) ou Replicate.");
            }

            // 5. Salva resultado
            var path = await SalvarResultadoAsync(resultadoUrl, ct);

            sw.Stop();
            _logger.LogInformation("═══ CONCLUÍDO em {Ms}ms → {Path} ═══", sw.ElapsedMilliseconds, path);

            return new SimulacaoResult
            {
                ImagemResultadoPath = path,
                ProviderUtilizado = req.Provider.ToString(),
                TempoProcessamentoMs = sw.ElapsedMilliseconds,
                Aviso = CombinarAvisos(string.IsNullOrEmpty(avisoQuadro) ? aviso : CombinarAvisos(aviso, avisoQuadro), $"Zona de edição (vermelho): {auditOverlay}")
            };
        }
        finally
        {
            if (maskPath is not null) LimparArquivosTemp(maskPath);
            if (zonaAjustadaPath is not null) LimparArquivosTemp(zonaAjustadaPath);
            if (guiaPath is not null) LimparArquivosTemp(guiaPath);
            if (imagemPrep is not null) LimparArquivosTemp(imagemPrep);
            _b64Cache.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  AJUSTE DE VOLUME (inalterado)
    // ═══════════════════════════════════════════════════════════
    public async Task<string> AjustarVolumeAsync(
        AjustarVolumeRequest req,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[VOLUME] Ajustando volume para nível {Nivel}...", req.Nivel);

        var outputFolder = GarantirPasta("wwwroot", "resultados");
        var masksFolder = GarantirPasta("wwwroot", "masks");

        string? maskPath = null;
        try
        {
            var caminhoImagem = ResolverCaminho(req.ImagemResultadoPath ?? req.ImagemOriginalPath!);
            var rosto = DetectarRostoSeguro(caminhoImagem);
            var comprimento = string.IsNullOrWhiteSpace(req.Comprimento) ? "60 cm" : req.Comprimento;

            var (caminhoMascara, _) = await HairMaskGenerator.GerarMascaraInteligenteAsync(
                caminhoImagem, masksFolder, rosto, comprimento, _http, _rep, _logger, ct);

            maskPath = caminhoMascara;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VOLUME] Não foi possível gerar máscara de cabelo; usando fallback.");
        }

        try
        {
            return await HairVolumeAdjuster.Aplicar(req, maskPath, _logger, outputFolder, ct);
        }
        finally
        {
            if (maskPath is not null)
                LimparArquivosTemp(maskPath);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  PIPELINE NOVO: Kontext (gera o cabelo) + composite (só dentro da zona)
    // ═══════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════
    //  OPENAI gpt-image-1 (mesma família do ChatGPT)
    // ═══════════════════════════════════════════════════════════
    private async Task<(string url, string? aviso)> PipelineOpenAiAsync(
        string imagemPath,
        SimulacaoRequest req,
        Rectangle faceRect,
        CancellationToken ct)
    {
        if (!_openAi.Configurado)
            throw new InvalidOperationException(
                "OpenAI:ApiToken não configurada. Defina appsettings, User Secrets ou OpenAI__ApiToken.");

        var prompt = OpenAiHairClient.BuildPromptChatGptStyle(
            req.Comprimento, req.Cor, req.TipoCabelo, req.Modo.ToString());
        _logger.LogInformation("[OPENAI] Prompt: {Prompt}", prompt);

        var bytes = await _openAi.EditarCabeloAsync(imagemPath, prompt, ct);

        var outFolder = GarantirPasta("wwwroot", "resultados");
        var nome = $"{Guid.NewGuid():N}.png";
        var finalPath = Path.Combine(outFolder, nome);
        await File.WriteAllBytesAsync(finalPath, bytes, ct);

        // Cópia raw para auditoria (mesmo arquivo base)
        var rawPath = Path.Combine(outFolder, $"raw_openai_{Path.GetFileNameWithoutExtension(nome)}.png");
        File.Copy(finalPath, rawPath, overwrite: true);
        _logger.LogInformation("[OPENAI] Resultado salvo (sem composição de rosto): {Path}", finalPath);

        // NÃO colar o rosto original por cima: isso causava "rosto duplicado"
        // quando o modelo deslocava levemente a face. O gpt-image já preserva identidade.
        // Se no futuro precisar de proteção extra, alinhar landmarks antes de compor.

        var rel = "resultados/" + nome;
        return (rel, "OpenAI gpt-image (resultado direto, sem overlay de rosto).");
    }

    private static async Task CriarMascaraProtecaoRostoAsync(
        string imagemPath, Rectangle face, string outPath, CancellationToken ct)
    {
        using var img = await Image.LoadAsync<Rgba32>(imagemPath, ct);
        int W = img.Width, H = img.Height;
        using var m = new Image<L8>(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                m[x, y] = new L8(255);

        float cx = face.X + face.Width / 2f;
        float cy = face.Y + face.Height * 0.48f;
        float rx = face.Width * 0.42f;
        float ry = face.Height * 0.48f;
        for (int y = Math.Max(0, (int)(cy - ry)); y <= Math.Min(H - 1, (int)(cy + ry)); y++)
            for (int x = Math.Max(0, (int)(cx - rx)); x <= Math.Min(W - 1, (int)(cx + rx)); x++)
            {
                float nx = (x - cx) / rx, ny = (y - cy) / ry;
                if (nx * nx + ny * ny <= 1f)
                    m[x, y] = new L8(0);
            }

        m.Mutate(c => c.GaussianBlur(2.5f));
        await m.SaveAsPngAsync(outPath, ct);
    }

    private async Task<(string url, string? aviso)> PipelineKontextHairAsync(
        string imagemPath,
        string zonaPath,
        string zonaAjustadaPath,
        string? guiaPath,
        SimulacaoRequest req,
        HairEditMode modo,
        bool foraDoQuadro,
        float yAlvoPx,
        Rectangle faceRect,
        CancellationToken ct)
    {
        var corPrompt = KontextPrompt.Cor(req.Cor, out var corMapeada);
        _logger.LogInformation("[COR] '{Original}' -> '{Prompt}' (reconhecida: {Ok})", req.Cor, corPrompt, corMapeada);
        if (!corMapeada)
            _logger.LogWarning("[COR] Cor não reconhecida: '{Cor}'. O texto original foi enviado ao modelo.", req.Cor);

        // MÉTODO (variável de ambiente MEGAHAIR_METODO): kontext (padrão) | flux2-guia | fill-mascara
        // Os dois últimos usam a máscara do comprimento EXATO pedido (só no modo Alongar).
        var metodo = (Environment.GetEnvironmentVariable("MEGAHAIR_METODO") ?? "kontext").Trim().ToLowerInvariant();
        if (guiaPath is null || modo != HairEditMode.Extend) metodo = "kontext";
        _logger.LogInformation("[METODO] {Metodo}", metodo);

        object body;
        string modeloChamada;
        string? guiaFotoPath = null;
        var semente = Random.Shared.Next(1, 999_999);
        switch (metodo)
        {
            case "flux2-guia":
                guiaFotoPath = await PintarGuiaAsync(imagemPath, guiaPath!, GarantirPasta("wwwroot", "temp"), ct);
                body = new
                {
                    input = new
                    {
                        prompt = KontextPrompt.BuildGuiado(req.Cor, req.TipoCabelo),
                        input_images = new[] { ConverterBase64(guiaFotoPath) },
                        aspect_ratio = "match_input_image",
                        output_format = "jpg",
                        safety_tolerance = 2,
                        seed = semente
                    }
                };
                modeloChamada = "black-forest-labs/flux-2-pro";
                break;

            case "fill-mascara":
                body = new
                {
                    input = new
                    {
                        image = ConverterBase64(imagemPath),
                        mask = ConverterBase64(guiaPath!),
                        prompt = KontextPrompt.BuildFill(req.Cor, req.TipoCabelo),
                        steps = Math.Clamp(_rep.FluxSteps, 15, 50),
                        guidance = Math.Clamp(_rep.FluxGuidance, 1.5d, 100d),
                        safety_tolerance = 2,
                        output_format = "png",
                        seed = semente
                    }
                };
                modeloChamada = $"{_rep.FluxFillOwner}/{_rep.FluxFillName}";
                break;

            default:
                var prompt = KontextPrompt.Build(req.Comprimento, req.Cor, req.TipoCabelo, req.MetodoMegaHair, modo, foraDoQuadro);
                _logger.LogInformation("[KONTEXT] Prompt: {Prompt}", prompt);
                body = new
                {
                    input = new
                    {
                        prompt,
                        input_image = ConverterBase64(imagemPath),
                        aspect_ratio = "match_input_image",
                        output_format = "png",
                        safety_tolerance = 2,
                        seed = semente
                    }
                };
                modeloChamada = KontextModel;
                break;
        }

        var tempFolder = GarantirPasta("wwwroot", "temp");
        string geradaPath;

        // MODO DE TESTE (não gasta créditos): defina a variável de ambiente MEGAHAIR_RAW_DEBUG com o
        // caminho de um raw_*.png já gerado para reaproveitar o resultado do Kontext e testar só o composite.
        var rawDebug = Environment.GetEnvironmentVariable("MEGAHAIR_RAW_DEBUG");
        if (!string.IsNullOrWhiteSpace(rawDebug) && File.Exists(rawDebug))
        {
            _logger.LogWarning("[DEBUG] Reutilizando resultado cru existente (sem chamar o Kontext): {Raw}", rawDebug);
            geradaPath = Path.Combine(tempFolder, $"inter_{Guid.NewGuid()}.png");
            File.Copy(rawDebug, geradaPath, overwrite: true);
        }
        else
        {
            string kontextUrl;
            try
            {
                kontextUrl = await ExecutarComRetryRateLimitAsync(body, ct, modeloChamada);
            }
            catch (InvalidOperationException ex) when (IsNsfw(ex))
            {
                throw new InvalidOperationException("A foto foi bloqueada pelo filtro de segurança do provedor. Tente outra foto.", ex);
            }

            geradaPath = await BaixarParaTempAsync(kontextUrl, tempFolder, ct);

            // Cópia do resultado cru do modelo, para você comparar com o final.
            var rawPath = Path.Combine(GarantirPasta("wwwroot", "resultados"), $"raw_{Guid.NewGuid():N}.png");
            File.Copy(geradaPath, rawPath, overwrite: true);
            _logger.LogInformation("[KONTEXT] Resultado cru salvo em {Raw}", rawPath);
        }

        // Alinha o resultado do modelo à foto original medindo corpo e fundo (fora da zona de edição).
        {
            var alinhada = await GeneratedAligner.AlinharAsync(
                imagemPath, geradaPath, zonaPath, tempFolder, _logger, ct);
            if (alinhada != geradaPath)
            {
                LimparArquivosTemp(geradaPath);
                geradaPath = alinhada;
            }
        }

        var medicao = await HairLengthProbe.MedirAsync(imagemPath, geradaPath, zonaPath, ct);
        _logger.LogInformation("[COMPRIMENTO] fim do cabelo gerado y={Fim} | alvo y={Alvo:F0} | base da foto y={H}",
            medicao.Fim?.ToString() ?? "n/d", yAlvoPx, medicao.Altura);

        _logger.LogInformation("[2/2] Compondo o resultado (original fora da zona, cabelo novo dentro dela).");
        // Alongar: zona ajustada (formato do pedido). Diminuir/Cor: zona ampla, para trocar também o cabelo antigo que passava dela.
        string maskParaComposite = metodo == "fill-mascara" ? guiaPath! : (modo == HairEditMode.Extend ? zonaAjustadaPath : zonaPath);
        string? finalMaskPath = null;
        string? avisoSeg = null;
        // Segmentação (Grounded-SAM) é lenta e instável: DESLIGADA por padrão.
        // Para ligar: variável de ambiente MEGAHAIR_SEGMENTATION=1
        bool usarSeg = Environment.GetEnvironmentVariable("MEGAHAIR_SEGMENTATION") == "1";
        if (!usarSeg)
            _logger.LogInformation("[COMPOSITE] Segmentação desligada: usando a zona ajustada.");
        else
            try
            {
                var origBytes = await File.ReadAllBytesAsync(imagemPath, ct);
                var genBytes = await File.ReadAllBytesAsync(geradaPath, ct);
                var info = Image.Identify(origBytes);
                var dbg = GarantirPasta("wwwroot", "masks");

                // Em sequência (e não em paralelo) para não estourar o limite de requisições do Replicate.
                using var hairOld = await _segmenter.SegmentAsync(origBytes, info.Width, info.Height, ct, dbg);
                using var hairNew = await _segmenter.SegmentAsync(genBytes, info.Width, info.Height, ct, dbg);
                using var zona = Image.Load<L8>(zonaPath);

                using var finalMask = HairCompositeMask.Build(
                    hairOld, hairNew, zona, Math.Max(1.5f, info.Width * 0.004f), out var frac);
                _logger.LogInformation("[COMPOSITE] area do cabelo a colar = {F:P1}", frac);
                if (frac < 0.002)
                    throw new InvalidOperationException("Máscara de cabelo vazia.");

                finalMaskPath = Path.Combine(dbg, $"final_{Guid.NewGuid():N}.png");
                await finalMask.SaveAsPngAsync(finalMaskPath, ct);
                maskParaComposite = finalMaskPath;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "[COMPOSITE] Segmentação indisponível; usando a zona ajustada (sem segmentação).");
                avisoSeg = "Segmentação do cabelo indisponível: composição aproximada.";
            }

        string composto;
        try
        {
            var comp = await ComposicaoSegura.ComporAsync(
                imagemPath, geradaPath, maskParaComposite, faceRect,
                GarantirPasta("wwwroot", "resultados"), ct);
            _logger.LogInformation(
                "[ROSTO] pixels alterados no núcleo do rosto = {N} (esperado: 0) | área colada = {P:P1}",
                comp.PixelsAlteradosNoRosto, comp.AreaColadaPct);
            composto = "resultados/" + Path.GetFileName(comp.CaminhoAbsoluto);
        }
        finally
        {
            if (finalMaskPath is not null) LimparArquivosTemp(finalMaskPath);
        }

        // EXTENSÃO ATÉ A BORDA (EXPERIMENTAL — FALHOU NO TESTE: a máscara por colunas saiu estreita e o Fill
        // inventou uma pessoa). DESLIGADA por padrão; só liga com MEGAHAIR_EXTENSAO=1. Não habilitar em produção.
        bool extensaoLigada = Environment.GetEnvironmentVariable("MEGAHAIR_EXTENSAO") == "1";
        if (extensaoLigada && foraDoQuadro && medicao.Fim is int fimMed && medicao.Colunas is not null
            && fimMed < medicao.Altura - (int)(medicao.Altura * 0.03f))
        {
            try
            {
                var baseAbs = ResolverSaida(composto);
                var novo = await ExtenderCabeloAteBordaAsync(baseAbs, medicao, req, tempFolder, ct);
                composto = File.Exists(composto) ? novo.Abs : novo.Rel;
                _logger.LogInformation("[EXTENSAO] cabelo continuado de y={De} até a borda y={Ate}", fimMed, medicao.Altura);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[EXTENSAO] falhou; mantendo o resultado sem extensão.");
            }
        }

        LimparArquivosTemp(geradaPath);
        if (guiaFotoPath is not null) LimparArquivosTemp(guiaFotoPath);
        return (composto, avisoSeg);
    }

    // ═══════════════════════════════════════════════════════════
    //  BENCHMARK (somente desenvolvimento): a MÁSCARA do comprimento pedido como guia/preenchimento
    // ═══════════════════════════════════════════════════════════
    private static async Task<string> PintarGuiaAsync(string fotoPath, string maskPath, string pasta, CancellationToken ct)
    {
        using var f = await Image.LoadAsync<Rgba32>(fotoPath, ct);
        using var m = await Image.LoadAsync<L8>(maskPath, ct);
        if (m.Width != f.Width || m.Height != f.Height) m.Mutate(c => c.Resize(f.Width, f.Height));
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                float a = m[x, y].PackedValue / 255f * 0.85f;
                if (a <= 0f) continue;
                var p = f[x, y];
                f[x, y] = new Rgba32((byte)(p.R * (1 - a)), (byte)(p.G * (1 - a) + 255 * a), (byte)(p.B * (1 - a) + 102 * a), 255);
            }
        var path = Path.Combine(pasta, $"guia_{Guid.NewGuid():N}.png");
        await f.SaveAsPngAsync(path, ct);
        return path;
    }

    public async Task<string> BenchmarkAsync(SimulacaoRequest req, string? modelos, CancellationToken ct)
    {
        var id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var pasta = GarantirPasta("wwwroot", "bench", id);
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var masksFolder = GarantirPasta("wwwroot", "masks");

        var imagemPrep = await ImagePreparer.PrepararParaOpenAiAsync(
            ResolverCaminho(req.ImagemOriginalPath), tempFolder, _rep.ImageSize);
        string? zonaPath = null, zonaAjPath = null, guiaPath = null, guiaFotoPath = null;
        try
        {
            var rosto = DetectarRostoSeguro(imagemPrep)
                ?? throw new InvalidOperationException("Não encontrei um rosto na foto.");
            var info = Image.Identify(imagemPrep);
            float fx = (float)rosto.X, fy = (float)rosto.Y, fw = (float)rosto.Width, fh = (float)rosto.Height;
            if (fw <= 1.5f && fh <= 1.5f) { fx *= info.Width; fw *= info.Width; fy *= info.Height; fh *= info.Height; }
            var faceRect = new Rectangle((int)fx, (int)fy, (int)fw, (int)fh);

            var mcm = System.Text.RegularExpressions.Regex.Match(req.Comprimento ?? "", @"\d+");
            int cm = mcm.Success && int.TryParse(mcm.Value, out var vcm) ? vcm : 60;

            zonaPath = await _zona.GerarAsync(imagemPrep, masksFolder, fx, fy, fw, fh, req.Comprimento, req.Modo, ct);
            zonaAjPath = await _zona.GerarAsync(imagemPrep, masksFolder, fx, fy, fw, fh, req.Comprimento, req.Modo, ct,
                ajustada: true, tipoCabelo: req.TipoCabelo);
            // máscara do comprimento EXATO pedido (catálogo), usada como guia e como máscara do Fill
            guiaPath = await _zona.GerarAsync(imagemPrep, masksFolder, fx, fy, fw, fh, req.Comprimento, req.Modo, ct,
                ajustada: true, tipoCabelo: req.TipoCabelo, cmExato: cm);
            guiaFotoPath = await PintarGuiaAsync(imagemPrep, guiaPath, tempFolder, ct);

            float chin = fy + fh, headH = fh * 1.45f, topo = chin - headH;
            float yAlvo = topo + cm * headH / 23f;
            bool fora = req.Modo == HairEditMode.Extend && yAlvo > info.Height;

            var promptGuia = KontextPrompt.BuildGuiado(req.Cor, req.TipoCabelo);
            var promptFill = KontextPrompt.BuildFill(req.Cor, req.TipoCabelo);
            var b64Guia = ConverterBase64(guiaFotoPath);
            var b64Orig = ConverterBase64(imagemPrep);
            var b64Mask = ConverterBase64(guiaPath);
            var fillModelo = $"{_rep.FluxFillOwner}/{_rep.FluxFillName}";
            const int semente = 12345;

            var todos = new List<(string Nome, string Modelo, object Corpo, string Zona)>
            {
                ("kontext-guia", "black-forest-labs/flux-kontext-pro",
                    new { input = new { prompt = promptGuia, input_image = b64Guia, aspect_ratio = "match_input_image", output_format = "png", safety_tolerance = 2, seed = semente } },
                    zonaAjPath),
                ("flux2-guia", "black-forest-labs/flux-2-pro",
                    new { input = new { prompt = promptGuia, input_images = new[] { b64Guia }, aspect_ratio = "match_input_image", output_format = "jpg", safety_tolerance = 2, seed = semente } },
                    zonaAjPath),
                ("fill-mascara", fillModelo,
                    new { input = new { image = b64Orig, mask = b64Mask, prompt = promptFill,
                                        steps = Math.Clamp(_rep.FluxSteps, 15, 50), guidance = Math.Clamp(_rep.FluxGuidance, 1.5d, 100d),
                                        safety_tolerance = 2, output_format = "png", seed = semente } },
                    guiaPath),
            };
            var pedidos = (modelos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var escolhidos = pedidos.Length == 0 ? todos
                : todos.Where(t => pedidos.Contains(t.Nome, StringComparer.OrdinalIgnoreCase)).ToList();

            File.Copy(imagemPrep, Path.Combine(pasta, "original.png"), overwrite: true);
            File.Copy(guiaFotoPath, Path.Combine(pasta, "guia.png"), overwrite: true);
            var linhas = new StringBuilder();

            foreach (var c in escolhidos)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var url = await ExecutarComRetryRateLimitAsync(c.Corpo, ct, c.Modelo);
                    var gerBytes = await _http.GetByteArrayAsync(url, ct);
                    var rawPng = Path.Combine(pasta, $"raw_{c.Nome}.png");
                    using (var tmp = Image.Load<Rgba32>(gerBytes)) await tmp.SaveAsPngAsync(rawPng, ct);

                    var alinhada = await GeneratedAligner.AlinharAsync(imagemPrep, rawPng, zonaPath, tempFolder, _logger, ct);
                    var med = await HairLengthProbe.MedirAsync(imagemPrep, alinhada, zonaPath, ct);
                    var comp = await ComposicaoSegura.ComporAsync(imagemPrep, alinhada, c.Zona, faceRect, pasta, ct,
                        nomeArquivo: $"final_{c.Nome}.png");
                    if (alinhada != rawPng) LimparArquivosTemp(alinhada);
                    sw.Stop();

                    var fim = med.Fim?.ToString() ?? "n/d";
                    var pct = med.Fim is int f ? $"{100.0 * f / info.Height:F0}% da altura" : "";
                    _logger.LogInformation("[BENCH] {Nome}: {S:F0}s | fim do cabelo y={Fim} ({Pct}) | alvo y={Alvo:F0} | base y={H} | rosto alterado={R}px",
                        c.Nome, sw.Elapsed.TotalSeconds, fim, pct, yAlvo, info.Height, comp.PixelsAlteradosNoRosto);
                    linhas.Append($"<tr><td><b>{c.Nome}</b><br>{sw.Elapsed.TotalSeconds:F0} s<br>fim do cabelo: y={fim}<br>{pct}<br>pixels do rosto alterados: {comp.PixelsAlteradosNoRosto}</td>" +
                                  $"<td><img src='raw_{c.Nome}.png'></td><td><img src='final_{c.Nome}.png'></td></tr>");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[BENCH] {Nome} falhou", c.Nome);
                    linhas.Append($"<tr><td><b>{c.Nome}</b></td><td colspan='2'>FALHOU: {WebUtility.HtmlEncode(ex.Message)}</td></tr>");
                }
            }

            var html = new StringBuilder();
            html.Append("<!doctype html><meta charset='utf-8'><title>Benchmark</title>");
            html.Append("<style>body{background:#111;color:#eee;font-family:sans-serif;padding:16px}table{border-collapse:collapse}td,th{border:1px solid #333;padding:8px;vertical-align:top}img{width:380px;display:block}</style>");
            html.Append($"<h2>Benchmark {id}</h2><p>Comprimento: {WebUtility.HtmlEncode(req.Comprimento)} | Cor: {WebUtility.HtmlEncode(req.Cor)} | Tipo: {WebUtility.HtmlEncode(req.TipoCabelo)} | modo: {req.Modo} | foto: {info.Width}x{info.Height} | alvo y={yAlvo:F0} | fora do quadro: {fora}</p>");
            html.Append($"<p><small>Prompt (guiado): {WebUtility.HtmlEncode(promptGuia)}</small></p><p><small>Prompt (Fill): {WebUtility.HtmlEncode(promptFill)}</small></p>");
            html.Append("<table><tr><th>Modelo</th><th>Resultado cru (alinhado)</th><th>Resultado final (composto)</th></tr>");
            html.Append("<tr><td><b>Original</b> e <b>guia</b> (máscara do comprimento pedido)</td><td><img src='original.png'></td><td><img src='guia.png'></td></tr>");
            html.Append(linhas);
            html.Append("</table>");
            await File.WriteAllTextAsync(Path.Combine(pasta, "index.html"), html.ToString(), ct);
            return $"/bench/{id}/index.html";
        }
        finally
        {
            LimparArquivosTemp(imagemPrep);
            if (zonaPath is not null) LimparArquivosTemp(zonaPath);
            if (zonaAjPath is not null) LimparArquivosTemp(zonaAjPath);
            if (guiaPath is not null) LimparArquivosTemp(guiaPath);
            if (guiaFotoPath is not null) LimparArquivosTemp(guiaFotoPath);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  EXTENSÃO DO CABELO ATÉ A BORDA DA FOTO
    // ═══════════════════════════════════════════════════════════
    private string ResolverSaida(string p)
    {
        if (File.Exists(p)) return p;
        var rel = p.TrimStart('/', '\\');
        foreach (var c in new[]
        {
            Path.Combine(_env.ContentRootPath, "wwwroot", rel),
            Path.Combine(_env.ContentRootPath, rel)
        })
            if (File.Exists(c)) return c;
        throw new FileNotFoundException("Resultado do composite não encontrado.", p);
    }

    private async Task<(string Abs, string Rel)> ExtenderCabeloAteBordaAsync(
        string compostoBase, HairLengthProbe.Medicao med, SimulacaoRequest req,
        string tempFolder, CancellationToken ct)
    {
        using var baseImg = await Image.LoadAsync<Rgba32>(compostoBase, ct);
        int W = baseImg.Width, H = baseImg.Height;
        int fim = (int)(med.Fim!.Value * (double)H / med.Altura);
        int y0 = Math.Max(0, fim - (int)(H * 0.03f));          // pequena sobreposição com o cabelo existente

        // máscara: colunas onde há cabelo no fim, de y0 até a borda inferior
        using var mask = new Image<L8>(W, H, new L8(0));
        for (int x = 0; x < W; x++)
        {
            int mx = Math.Min(med.Largura - 1, (int)((long)x * med.Largura / W));
            if (!med.Colunas![mx]) continue;
            for (int y = y0; y < H; y++) mask[x, y] = new L8(255);
        }
        mask.Mutate(c => c.GaussianBlur(2f));
        var maskPath = Path.Combine(tempFolder, $"ext_mask_{Guid.NewGuid():N}.png");
        await mask.SaveAsPngAsync(maskPath, ct);

        var corEn = KontextPrompt.Cor(req.Cor, out _);
        var tex = KontextPrompt.Textura(req.TipoCabelo);
        var prompt = $"{corEn} hair with {tex}, the same strands of hair continuing straight down over the chest " +
                     "and shoulders, same color, texture and thickness as the hair above. Photograph.";

        var body = new
        {
            input = new
            {
                image = ConverterBase64(compostoBase),
                mask = ConverterBase64(maskPath),
                prompt,
                steps = Math.Clamp(_rep.FluxSteps, 15, 50),
                guidance = Math.Clamp(_rep.FluxGuidance, 1.5d, 100d),
                safety_tolerance = 2,
                output_format = "png",
                seed = Random.Shared.Next(1, 999_999)
            }
        };

        string fillPath;
        try
        {
            var url = await ExecutarComRetryRateLimitAsync(body, ct);   // modelo = FluxFillOwner/FluxFillName
            fillPath = await BaixarParaTempAsync(url, tempFolder, ct);
        }
        finally { LimparArquivosTemp(maskPath); }

        try
        {
            using var fill = await Image.LoadAsync<Rgba32>(fillPath, ct);
            if (fill.Width != W || fill.Height != H) fill.Mutate(c => c.Resize(W, H));

            // composite: só a máscara (suavizada) vem do Fill; o resto permanece idêntico
            for (int y = y0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float a = mask[x, y].PackedValue / 255f;
                    if (a <= 0f) continue;
                    var o = baseImg[x, y]; var r = fill[x, y];
                    baseImg[x, y] = new Rgba32(
                        (byte)(o.R + (r.R - o.R) * a),
                        (byte)(o.G + (r.G - o.G) * a),
                        (byte)(o.B + (r.B - o.B) * a), 255);
                }
        }
        finally { LimparArquivosTemp(fillPath); }

        var nome = $"{Guid.NewGuid():N}.png";
        var abs = Path.Combine(GarantirPasta("wwwroot", "resultados"), nome);
        await baseImg.SaveAsPngAsync(abs, ct);
        return (abs, $"resultados/{nome}");
    }

    // ═══════════════════════════════════════════════════════════
    //  REPLICATE HELPERS
    // ═══════════════════════════════════════════════════════════
    private async Task<string> ExecutarComRetryRateLimitAsync(
        object body, CancellationToken ct, string? modelo = null)
    {
        for (int t = 1; t <= _rep.MaxRateLimitRetries; t++)
        {
            try
            {
                return await EnviarPredicaoAsync(body, ct, modelo);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = CalcularDelay(ex, t);
                _logger.LogWarning(
                    "[429] Rate limit — aguardando {D}s (tentativa {T}/{Max})",
                    delay.TotalSeconds.ToString("F0"), t, _rep.MaxRateLimitRetries);

                if (t >= _rep.MaxRateLimitRetries)
                    throw new InvalidOperationException(
                        $"Rate limit persistente após {t} tentativas.", ex);

                await Task.Delay(delay, ct);
            }
        }

        throw new InvalidOperationException("Fluxo inesperado.");
    }

    private async Task<string> EnviarPredicaoAsync(object body, CancellationToken ct, string? modelo = null)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = modelo is null
            ? $"{ReplicateBaseUrl}/models/{_rep.FluxFillOwner}/{_rep.FluxFillName}/predictions"
            : $"{ReplicateBaseUrl}/models/{modelo}/predictions";

        using var req = CriarReplicateRequest(HttpMethod.Post, url);
        req.Content = content;

        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Falha HTTP do provedor de IA: {Status} {Body}", resp.StatusCode, err);

            var mensagem = resp.StatusCode == HttpStatusCode.TooManyRequests
                ? err
                : "Falha ao comunicar com o provedor de IA.";

            throw new HttpRequestException(mensagem, null, resp.StatusCode);
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var predId = doc.RootElement.GetProperty("id").GetString()!;

        return await PollResultadoAsync(predId, ct);
    }

    private async Task<string> PollResultadoAsync(string predId, CancellationToken ct)
    {
        for (int i = 0; i < _rep.MaxPollAttempts; i++)
        {
            await Task.Delay(_rep.PollIntervalMs, ct);

            using var req = CriarReplicateRequest(HttpMethod.Get,
                $"{ReplicateBaseUrl}/predictions/{predId}");
            using var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Falha HTTP ao consultar predição {PredId}: {Status}",
                    predId, resp.StatusCode);

                throw new HttpRequestException(
                    "Falha ao consultar o processamento da imagem.",
                    null,
                    resp.StatusCode);
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "Resposta inválida do provedor de IA.");
            }

            var status = statusElement.GetString();

            if (i % 5 == 0) _logger.LogDebug("[Poll {I}] {Status}", i, status);

            switch (status)
            {
                case "succeeded":
                    return ExtrairOutput(doc.RootElement);
                case "failed":
                case "canceled":
                    var err = doc.RootElement.TryGetProperty("error", out var e)
                        ? e.GetString() : "sem detalhe";
                    _logger.LogError("Predição {PredId} {Status}: {Erro}", predId, status, err);
                    throw new InvalidOperationException("A geração da imagem não foi concluída.");
            }
        }

        throw new TimeoutException($"Timeout após {_rep.MaxPollAttempts} polls.");
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS GERAIS
    // ═══════════════════════════════════════════════════════════
    private TimeSpan CalcularDelay(HttpRequestException ex, int tentativa)
    {
        var retryAfter = ExtrairRetryAfter(ex);
        var backoff = (int)(_rep.RateLimitBaseDelayMs * Math.Pow(2, tentativa - 1));
        var jitter = Random.Shared.Next((int)(-backoff * 0.2), (int)(backoff * 0.2));
        var total = Math.Max(retryAfter * 1_000, backoff + jitter);
        return TimeSpan.FromMilliseconds(Math.Min(total, _rep.RateLimitMaxDelayMs));
    }

    private static int ExtrairRetryAfter(HttpRequestException ex)
    {
        try
        {
            var msg = ex.Message;
            var idx = msg.IndexOf("\"retry_after\":", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + "\"retry_after\":".Length;
                var span = msg.AsSpan(start).TrimStart();
                var end = span.IndexOfAny("},\n ".AsSpan());
                if (end > 0 && int.TryParse(span[..end].Trim(), out var s)) return s;
            }
        }
        catch { /* ignore */ }
        return 5;
    }

    private HttpRequestMessage CriarReplicateRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _rep.ApiToken);
        return req;
    }

    private string ConverterBase64(string caminho)
    {
        if (_b64Cache.TryGetValue(caminho, out var c)) return c;
        var bytes = File.ReadAllBytes(caminho);
        var ext = Path.GetExtension(caminho).TrimStart('.').ToLowerInvariant();
        var mime = ext is "jpg" or "jpeg" ? "image/jpeg"
                  : ext is "webp" ? "image/webp" : "image/png";
        var r = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        _b64Cache[caminho] = r;
        return r;
    }

    private async Task<string> BaixarParaTempAsync(string url, string pasta, CancellationToken ct)
    {
        var bytes = await _http.GetByteArrayAsync(url, ct);
        var path = Path.Combine(pasta, $"inter_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    private async Task<string> SalvarResultadoAsync(string url, CancellationToken ct)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return url;

        var bytes = await _http.GetByteArrayAsync(url, ct);
        var pasta = GarantirPasta("wwwroot", "resultados");
        var nome = $"{Guid.NewGuid()}.png";
        await File.WriteAllBytesAsync(Path.Combine(pasta, nome), bytes, ct);
        return $"resultados/{nome}";
    }

    private string GarantirPasta(params string[] partes)
    {
        var caminho = Path.Combine(new[] { _env.ContentRootPath }.Concat(partes).ToArray());
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    private string ResolverCaminho(string path)
    {
        var abs = Path.IsPathRooted(path) ? path : Path.Combine(_env.ContentRootPath, path);
        if (!File.Exists(abs))
            throw new FileNotFoundException("Imagem não encontrada.", abs);
        return abs;
    }

    private FaceBox? DetectarRostoSeguro(string imagemPath)
    {
        try
        {
            var model = LocalizarModeloOnnx();
            using var d = new FaceDetector(model);
            var r = d.DetectarRosto(imagemPath);
            _logger.LogInformation(r != null
                ? "Rosto detectado ({C:P0})" : "Rosto não detectado",
                r?.Confidence);
            return r;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Detecção falhou — prosseguindo");
            return null;
        }
    }

    private string LocalizarModeloOnnx()
    {
        const string nome = "ultraface.onnx";
        foreach (var c in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models", nome),
            Path.Combine(_env.ContentRootPath,    "Models", nome),
        })
        {
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException($"'{nome}' não encontrado.");
    }

    private void LimparArquivosTemp(params string[] caminhos)
    {
        foreach (var c in caminhos)
            try { if (File.Exists(c)) File.Delete(c); }
            catch (Exception ex) { _logger.LogWarning(ex, "Não deletou: {P}", c); }
    }

    private static string ExtrairOutput(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var o))
            throw new InvalidOperationException(
                "Resposta inválida do provedor de IA.");

        var output = o.ValueKind switch
        {
            JsonValueKind.Array when o.GetArrayLength() > 0
                => o[0].GetString(),
            JsonValueKind.String => o.GetString(),
            _ => throw new InvalidOperationException("Resposta inválida do provedor de IA.")
        };

        if (string.IsNullOrWhiteSpace(output)
            || !Uri.TryCreate(output, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Resposta inválida do provedor de IA.");
        }

        return output;
    }

    private static bool IsNsfw(Exception ex) =>
        ex.Message.Contains("NSFW", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static string CombinarAvisos(string? a, string b) =>
        string.IsNullOrWhiteSpace(a) ? b : $"{a} {b}";
}
