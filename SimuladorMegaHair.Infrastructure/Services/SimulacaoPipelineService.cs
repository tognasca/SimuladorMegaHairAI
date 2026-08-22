using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class SimulacaoPipelineService : IImageSimulationService
{
    private readonly HttpClient _http;
    private readonly ReplicateOptions _rep;
    private readonly OpenAIOptions _oai;
    private readonly SimulacaoOptions _sim;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SimulacaoPipelineService> _logger;
    private readonly Dictionary<string, string> _b64Cache = new();

    private static readonly Random _rng = new();
    private const string ReplicateBaseUrl = "https://api.replicate.com/v1";
    private const string OpenAIBaseUrl = "https://api.openai.com/v1";

    public SimulacaoPipelineService(
        HttpClient http,
        IOptions<ReplicateOptions> rep,
        IOptions<OpenAIOptions> oai,
        IOptions<SimulacaoOptions> sim,
        IWebHostEnvironment env,
        ILogger<SimulacaoPipelineService> logger)
    {
        _http = http;
        _rep = rep.Value;
        _oai = oai.Value;
        _sim = sim.Value;
        _env = env;
        _logger = logger;
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

        _logger.LogInformation(
            "═══ SIMULAÇÃO [{Provider}] ═══", req.Provider);

        // 1. Resolve e prepara imagem
        var imagemAbs = ResolverCaminho(req.ImagemOriginalPath);
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var imagemPrep = await ImagePreparer.PrepararParaOpenAiAsync(
            imagemAbs, tempFolder, _rep.ImageSize);

        // 2. Detecta rosto na imagem já recortada (mesmas coordenadas da máscara)
        var rosto = DetectarRostoSeguro(imagemPrep);

        // 3. Gera máscara (MediaPipe → SAM2 ou fallback local)
        var masksFolder = GarantirPasta("wwwroot", "masks");
        var maskPath = await HairMaskGenerator.GerarMascaraCabeloReplicateAsync(
            imagemPrep, masksFolder, rosto, ct);

        var auditOverlay = await HairMaskAudit.SalvarAsync(
            imagemPrep, maskPath,
            GarantirPasta("wwwroot", "masks", "audit"),
            _logger, ct);

        // 4. Escolhe pipeline
        var (resultadoUrl, aviso) = req.Provider switch
        {
            ImageProvider.Local => await PipelineLocalAsync(
                                           imagemPrep, maskPath, req, ct),
            ImageProvider.Replicate => await PipelineReplicateAsync(
                                           imagemPrep, maskPath, req, ct),
            ImageProvider.OpenAI => await PipelineOpenAIAsync(
                                           imagemPrep, maskPath, req, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(req.Provider))
        };

        // 5. Salva resultado
        var path = await SalvarResultadoAsync(resultadoUrl, ct);

        LimparArquivosTemp(maskPath, imagemPrep);
        _b64Cache.Clear();

        sw.Stop();
        _logger.LogInformation(
            "═══ CONCLUÍDO em {Ms}ms → {Path} ═══",
            sw.ElapsedMilliseconds, path);

        return new SimulacaoResult
        {
            ImagemResultadoPath = path,
            ProviderUtilizado = req.Provider.ToString(),
            TempoProcessamentoMs = sw.ElapsedMilliseconds,
            Aviso = CombinarAvisos(aviso, $"Máscara (vermelho = área gerada): {auditOverlay}")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  PIPELINE LOCAL (gratuito)
    //  Flux Fill local via ONNX / Stable Diffusion local
    // ═══════════════════════════════════════════════════════════

    private async Task<(string url, string? aviso)> PipelineLocalAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        CancellationToken ct)
    {
        _logger.LogInformation("[LOCAL] Iniciando pipeline gratuito...");

        // Tenta usar modelo local ONNX
        // Se não tiver modelo local, faz inpainting simples com ImageSharp
        try
        {
            var resultPath = await LocalHairInpainter.AplicarCabeloAsync(
                imagemPath,
                maskPath,
                req.Cor,
                req.TipoCabelo,
                req.Comprimento,
                GarantirPasta("wwwroot", "resultados"),
                ct);

            _logger.LogInformation("[LOCAL] ✓ Concluído");

            // Retorna path local como "url"
            return (resultPath, "Simulação local — qualidade básica.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[LOCAL] Falha no inpainting local — fazendo fallback para sobreposição de cor");

            var resultPath = await LocalHairColorizer.AplicarCorAsync(
                imagemPath,
                maskPath,
                req.Cor,
                GarantirPasta("wwwroot", "resultados"),
                ct);

            return (resultPath,
                "Simulação básica de cor — para resultado mais realista, " +
                "use o provider Replicate.");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  PIPELINE REPLICATE (pago)
    //  Flux Fill → freeze de identidade (pixels originais fora da máscara)
    // ═══════════════════════════════════════════════════════════

    private async Task<(string url, string? aviso)> PipelineReplicateAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        CancellationToken ct)
    {
        _logger.LogInformation("[REPLICATE] Iniciando pipeline pago...");

        var fluxVer = await ObterVersaoAsync(_rep.FluxFillOwner, _rep.FluxFillName, ct);

        _logger.LogInformation("[1/2] Flux Fill — gerando novo cabelo...");
        var fluxUrl = await ExecutarFluxFillAsync(
            imagemPath, maskPath, req, fluxVer, ct);

        _logger.LogInformation("[2/2] Freeze — restaurando rosto, roupa e fundo da foto original...");
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var geradaPath = await BaixarParaTempAsync(fluxUrl, tempFolder, ct);

        var composto = await IdentityCompositor.ComporPreservandoIdentidadeAsync(
            imagemPath,
            geradaPath,
            maskPath,
            GarantirPasta("wwwroot", "resultados"),
            (float)_rep.MaskFeatherSigma,
            ct);

        LimparArquivosTemp(geradaPath);

        _logger.LogInformation("[REPLICATE] ✓ Concluído");
        return (composto, null);
    }

    private async Task<string> ExecutarFluxFillAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        string versao,
        CancellationToken ct)
    {
        var fallbacks = PromptBuilder.BuildFallbacks(
            req.Comprimento, req.Cor, req.TipoCabelo);

        Exception? ultimo = null;

        foreach (var (prompt, negative) in fallbacks)
        {
            try
            {
                _logger.LogDebug("Prompt: {P}", prompt);

                var body = new
                {
                    version = versao,
                    input = new
                    {
                        image = ConverterBase64(imagemPath),
                        mask = ConverterBase64(maskPath),
                        prompt = prompt,
                        steps = _rep.FluxSteps,
                        guidance = _rep.FluxGuidance,
                        output_format = "png",
                        output_quality = 90,
                        seed = _rng.Next(1, 999_999)
                    }
                };

                return await ExecutarComRetryRateLimitAsync(body, ct);
            }
            catch (InvalidOperationException ex) when (IsNsfw(ex))
            {
                ultimo = ex;
                _logger.LogWarning("NSFW — tentando próximo prompt...");
                await Task.Delay(2_000, ct);
            }
        }

        throw new InvalidOperationException(
            "Imagem bloqueada pelo filtro de segurança. " +
            "Use uma foto com boa iluminação e fundo neutro.", ultimo);
    }

    // ═══════════════════════════════════════════════════════════
    //  PIPELINE OPENAI (pago)
    //  GPT Image Edit (gpt-image-1)
    // ═══════════════════════════════════════════════════════════

    private async Task<(string url, string? aviso)> PipelineOpenAIAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        CancellationToken ct)
    {
        _logger.LogInformation("[OPENAI] Iniciando pipeline GPT Image Edit...");

        if (string.IsNullOrWhiteSpace(_oai.ApiToken))
            throw new InvalidOperationException(
                "OpenAI:ApiToken não configurada.");

        var prompt = PromptBuilder.BuildOpenAI(
            req.Comprimento, req.Cor, req.TipoCabelo, req.MetodoMegaHair);

        _logger.LogDebug("Prompt OpenAI: {P}", prompt);

        using var form = new MultipartFormDataContent();

        // Imagem original
        var imgBytes = await File.ReadAllBytesAsync(imagemPath, ct);
        var imgContent = new ByteArrayContent(imgBytes);
        imgContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(imgContent, "image", "image.png");

        // Máscara
        var maskBytes = await File.ReadAllBytesAsync(maskPath, ct);
        var maskContent = new ByteArrayContent(maskBytes);
        maskContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(maskContent, "mask", "mask.png");

        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent(_oai.Model), "model");
        form.Add(new StringContent("1"), "n");
        form.Add(new StringContent(_oai.Size), "size");
        form.Add(new StringContent(_oai.Quality), "quality");
        form.Add(new StringContent("b64_json"), "response_format");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{OpenAIBaseUrl}/images/edits");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _oai.ApiToken);
        request.Content = form;

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"OpenAI retornou {response.StatusCode}: {err}",
                null, response.StatusCode);
        }

        var respJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);

        var b64 = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("b64_json")
            .GetString()!;

        // Salva diretamente do base64
        var pasta = GarantirPasta("wwwroot", "resultados");
        var nome = $"{Guid.NewGuid()}.png";
        var fullPath = Path.Combine(pasta, nome);

        await File.WriteAllBytesAsync(fullPath,
            Convert.FromBase64String(b64), ct);

        var composto = await IdentityCompositor.ComporPreservandoIdentidadeAsync(
            imagemPath,
            fullPath,
            maskPath,
            pasta,
            (float)_rep.MaskFeatherSigma,
            ct);

        LimparArquivosTemp(fullPath);

        _logger.LogInformation("[OPENAI] ✓ Concluído");
        return (composto, null);
    }

    // ═══════════════════════════════════════════════════════════
    //  REPLICATE HELPERS
    // ═══════════════════════════════════════════════════════════

    private async Task<string> ObterVersaoAsync(
        string owner, string name, CancellationToken ct)
    {
        using var req = CriarReplicateRequest(HttpMethod.Get,
            $"{ReplicateBaseUrl}/models/{owner}/{name}");
        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Modelo '{owner}/{name}' não encontrado ({resp.StatusCode}). {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("latest_version", out var latest))
            throw new InvalidOperationException(
                $"Modelo '{owner}/{name}' sem versão publicada.");

        return latest.GetProperty("id").GetString()!;
    }

    private async Task<string> ExecutarComRetryRateLimitAsync(
        object body, CancellationToken ct)
    {
        for (int t = 1; t <= _rep.MaxRateLimitRetries; t++)
        {
            try
            {
                return await EnviarPredicaoAsync(body, ct);
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

    private async Task<string> EnviarPredicaoAsync(object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var req = CriarReplicateRequest(
            HttpMethod.Post, $"{ReplicateBaseUrl}/predictions");
        req.Content = content;

        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Replicate {resp.StatusCode}: {err}", null, resp.StatusCode);
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var predId = doc.RootElement.GetProperty("id").GetString()!;

        return await PollResultadoAsync(predId, ct);
    }

    private async Task<string> PollResultadoAsync(
        string predId, CancellationToken ct)
    {
        for (int i = 0; i < _rep.MaxPollAttempts; i++)
        {
            await Task.Delay(_rep.PollIntervalMs, ct);

            using var req = CriarReplicateRequest(HttpMethod.Get,
                $"{ReplicateBaseUrl}/predictions/{predId}");
            using var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (i % 5 == 0) _logger.LogDebug("[Poll {I}] {Status}", i, status);

            switch (status)
            {
                case "succeeded":
                    return ExtrairOutput(doc.RootElement);
                case "failed":
                case "canceled":
                    var err = doc.RootElement.TryGetProperty("error", out var e)
                        ? e.GetString() : "sem detalhe";
                    throw new InvalidOperationException(
                        $"Predição {predId} {status}: {err}");
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
        var jitter = _rng.Next((int)(-backoff * 0.2), (int)(backoff * 0.2));
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
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _rep.ApiToken);
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

    private async Task<string> BaixarParaTempAsync(
        string url, string pasta, CancellationToken ct)
    {
        var bytes = await _http.GetByteArrayAsync(url, ct);
        var path = Path.Combine(pasta, $"inter_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    private async Task<string> SalvarResultadoAsync(string url, CancellationToken ct)
    {
        // Se for path local (pipeline local), retorna direto
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
        var caminho = Path.Combine(
            new[] { _env.ContentRootPath }.Concat(partes).ToArray());
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    private string ResolverCaminho(string path)
    {
        var abs = Path.IsPathRooted(path)
            ? path : Path.Combine(_env.ContentRootPath, path);
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
            catch (Exception ex)
            { _logger.LogWarning(ex, "Não deletou: {P}", c); }
    }

    private static string ExtrairOutput(JsonElement root)
    {
        var o = root.GetProperty("output");
        return o.ValueKind switch
        {
            JsonValueKind.Array => o[0].GetString()!,
            JsonValueKind.String => o.GetString()!,
            _ => throw new InvalidOperationException($"Output inesperado: {o.ValueKind}")
        };
    }

    private static bool IsNsfw(Exception ex) =>
        ex.Message.Contains("NSFW", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static string CombinarAvisos(string? a, string b) =>
        string.IsNullOrWhiteSpace(a) ? b : $"{a} {b}";
}