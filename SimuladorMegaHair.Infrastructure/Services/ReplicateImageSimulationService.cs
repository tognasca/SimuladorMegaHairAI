using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class ReplicateImageSimulationService : IImageSimulationService
{
    // ═══ Dependências ═══════════════════════════════════════════
    private readonly HttpClient _http;
    private readonly ReplicateOptions _opts;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ReplicateImageSimulationService> _logger;
    private readonly Dictionary<string, string> _base64Cache = new();

    private static readonly Random _jitter = new();
    private const string BaseUrl = "https://api.replicate.com/v1";

    public ReplicateImageSimulationService(
        HttpClient http,
        IOptions<ReplicateOptions> opts,
        IWebHostEnvironment env,
        ILogger<ReplicateImageSimulationService> logger)
    {
        _http = http;
        _opts = opts.Value;
        _env = env;
        _logger = logger;
        ValidarOpcoes();
    }

    // ═══════════════════════════════════════════════════════════════
    //  PÚBLICO
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> GerarSimulacaoAsync(
        string imagemOriginalPath,
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair,
        CancellationToken ct = default)
    {
        _logger.LogInformation("═══ SIMULAÇÃO INICIADA ═══");
        _base64Cache.Clear();

        var imagemAbsoluta = ResolverCaminho(imagemOriginalPath);
        var rosto = DetectarRostoSeguro(imagemAbsoluta);
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var masksFolder = GarantirPasta("wwwroot", "masks");

        var imagemPreparada = await ImagePreparer.PrepararParaOpenAiAsync(
            imagemAbsoluta, tempFolder, _opts.ImageSize);

        var inpaintingVer = await ObterVersaoAtualAsync(
            _opts.InpaintingOwner, _opts.InpaintingName, ct);

        var maskPath = await HairMaskGenerator.GerarMascaraCabeloReplicateAsync(
            imagemPreparada, masksFolder, rosto, ct);

        // Inpainting com retry de prompt
        var cabeloUrl = await ExecutarInpaintingComRetryAsync(
            imagemPreparada, maskPath,
            comprimento, cor, tipoCabelo, metodoMegaHair,
            inpaintingVer, ct);

        var resultadoPath = await SalvarResultadoAsync(cabeloUrl, ct);

        LimparArquivosTemp(maskPath, imagemPreparada);
        _base64Cache.Clear();

        _logger.LogInformation("═══ CONCLUÍDO → {Path} ═══", resultadoPath);
        return resultadoPath;
    }

    // ═══════════════════════════════════════════════════════════════
    //  INPAINTING COM RETRY DE PROMPT
    // ═══════════════════════════════════════════════════════════════

    private async Task<string> ExecutarInpaintingComRetryAsync(
        string imagemPath,
        string maskPath,
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair,
        string versao,
        CancellationToken ct)
    {
        // Prompts do mais elaborado ao mais simples
        var prompts = new[]
        {
            (
                prompt:   PromptBuilder.Build(comprimento, cor, tipoCabelo, metodoMegaHair),
                negative: PromptBuilder.BuildNegative()
            ),
            (
                prompt:   $"{TraduzirCorSimples(cor)} hair extensions, " +
                          "hair salon product photo, white background",
                negative: "person, face, body, nsfw, nude, skin, cartoon, blurry"
            ),
            (
                prompt:   "hair texture, natural hair, product photography",
                negative: "nsfw, nude, person, face, body, cartoon"
            )
        };

        Exception? ultimo = null;

        for (int i = 0; i < prompts.Length; i++)
        {
            try
            {
                _logger.LogInformation(
                    "[Tentativa {N}/{Max}] prompt: \"{P}\"",
                    i + 1, prompts.Length, prompts[i].prompt);

                var body = CriarBodyInpainting(
                    imagemPath, maskPath,
                    prompts[i].prompt,
                    prompts[i].negative,
                    versao);

                return await ExecutarComRetryRateLimitAsync(body, ct);
            }
            catch (InvalidOperationException ex) when (IsNsfwError(ex))
            {
                ultimo = ex;
                _logger.LogWarning(
                    "NSFW bloqueado (tentativa {N}/{Max})",
                    i + 1, prompts.Length);

                if (i < prompts.Length - 1)
                    await Task.Delay(2_000, ct);
            }
        }

        throw new InvalidOperationException(
            "A imagem foi bloqueada pelo filtro de segurança em todas as tentativas. " +
            "Use uma foto com boa iluminação e fundo neutro.", ultimo);
    }

    private object CriarBodyInpainting(
        string imagemPath,
        string maskPath,
        string prompt,
        string negative,
        string versao)
    {
        var imagemB64 = ConverterParaBase64Sincrono(imagemPath);
        var mascaraB64 = ConverterParaBase64Sincrono(maskPath);

        return new
        {
            version = versao,
            input = new
            {
                image = imagemB64,
                mask = mascaraB64,
                prompt = prompt,
                negative_prompt = negative,
                num_inference_steps = _opts.InpaintingSteps,
                guidance_scale = _opts.GuidanceScale,
                seed = Random.Shared.Next(1, 999_999)
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  REPLICATE API
    // ═══════════════════════════════════════════════════════════════

    private async Task<string> ObterVersaoAtualAsync(
        string owner, string name, CancellationToken ct)
    {
        var url = $"{BaseUrl}/models/{owner}/{name}";

        using var req = CriarRequest(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Modelo '{owner}/{name}' não encontrado " +
                $"({resp.StatusCode}). {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("latest_version", out var latest))
            throw new InvalidOperationException(
                $"Modelo '{owner}/{name}' sem versão publicada.");

        return latest.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Versão com ID nulo.");
    }

    private async Task<string> ExecutarComRetryRateLimitAsync(
        object body, CancellationToken ct)
    {
        for (int tentativa = 1; tentativa <= _opts.MaxRateLimitRetries; tentativa++)
        {
            try
            {
                return await EnviarPredicaoAsync(body, ct);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = CalcularDelayRateLimit(ex, tentativa);

                _logger.LogWarning(
                    "[429] Rate limit — aguardando {Delay}s (tentativa {T}/{Max})",
                    delay.TotalSeconds.ToString("F0"),
                    tentativa,
                    _opts.MaxRateLimitRetries);

                if (tentativa >= _opts.MaxRateLimitRetries)
                    throw new InvalidOperationException(
                        $"Rate limit persistente após {_opts.MaxRateLimitRetries} tentativas.", ex);

                await Task.Delay(delay, ct);
            }
        }

        throw new InvalidOperationException("Fluxo inesperado no rate limit retry.");
    }

    private async Task<string> EnviarPredicaoAsync(object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var req = CriarRequest(HttpMethod.Post, $"{BaseUrl}/predictions");
        req.Content = content;

        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Replicate retornou {resp.StatusCode}: {err}",
                inner: null,
                statusCode: resp.StatusCode);
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var predId = doc.RootElement.GetProperty("id").GetString()!;

        return await AguardarResultadoAsync(predId, ct);
    }

    private async Task<string> AguardarResultadoAsync(
        string predId, CancellationToken ct)
    {
        for (int i = 0; i < _opts.MaxPollAttempts; i++)
        {
            await Task.Delay(_opts.PollIntervalMs, ct);

            using var req = CriarRequest(HttpMethod.Get,
                $"{BaseUrl}/predictions/{predId}");
            using var resp = await _http.SendAsync(req, ct);

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var status = doc.RootElement.GetProperty("status").GetString();

            if (i % 5 == 0)
                _logger.LogDebug("[Poll {I}] status={Status}", i, status);

            switch (status)
            {
                case "succeeded":
                    return ExtrairOutput(doc.RootElement);

                case "failed":
                case "canceled":
                    var erro = doc.RootElement.TryGetProperty("error", out var e)
                        ? e.GetString() : "sem detalhe";
                    throw new InvalidOperationException(
                        $"Predição {predId} {status}: {erro}");
            }
        }

        throw new TimeoutException(
            $"Timeout após {_opts.MaxPollAttempts} polls.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private TimeSpan CalcularDelayRateLimit(HttpRequestException ex, int tentativa)
    {
        var retryAfter = ExtrairRetryAfter(ex);
        var baseMs = _opts.RateLimitBaseDelayMs;
        var backoff = (int)(baseMs * Math.Pow(2, tentativa - 1));
        var jitter = _jitter.Next((int)(-backoff * 0.2), (int)(backoff * 0.2));
        var totalMs = Math.Max(retryAfter * 1_000, backoff + jitter);
        return TimeSpan.FromMilliseconds(Math.Min(totalMs, _opts.RateLimitMaxDelayMs));
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
                var end = span.IndexOfAny('}', ',', '\n');
                if (end > 0 && int.TryParse(span[..end].Trim(), out var s))
                    return s;
            }

            var mIdx = msg.IndexOf("resets in ~", StringComparison.Ordinal);
            if (mIdx >= 0)
            {
                var start = mIdx + "resets in ~".Length;
                var end = msg.IndexOf('s', start);
                if (end > start &&
                    int.TryParse(msg[start..end].Trim(), out var secs))
                    return secs;
            }
        }
        catch { /* usa fallback */ }

        return 5;
    }

    private string ResolverCaminho(string path)
    {
        var abs = Path.IsPathRooted(path)
            ? path
            : Path.Combine(_env.ContentRootPath, path);

        if (!File.Exists(abs))
            throw new FileNotFoundException("Imagem não encontrada.", abs);

        return abs;
    }

    private FaceBox? DetectarRostoSeguro(string imagemPath)
    {
        try
        {
            var modelPath = LocalizarModeloOnnx();
            using var detector = new FaceDetector(modelPath);
            var rosto = detector.DetectarRosto(imagemPath);

            _logger.LogInformation(rosto != null
                ? "Rosto detectado ({C:P0})"
                : "Rosto não detectado — prosseguindo",
                rosto?.Confidence);

            return rosto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha na detecção — prosseguindo sem ela");
            return null;
        }
    }

    private async Task<string> SalvarResultadoAsync(string url, CancellationToken ct)
    {
        var bytes = await _http.GetByteArrayAsync(url, ct);
        var pasta = GarantirPasta("wwwroot", "resultados");
        var nome = $"{Guid.NewGuid()}.png";
        await File.WriteAllBytesAsync(Path.Combine(pasta, nome), bytes, ct);
        return $"resultados/{nome}";
    }

    private async Task<string> BaixarParaTempAsync(
        string url, string pasta, CancellationToken ct)
    {
        var bytes = await _http.GetByteArrayAsync(url, ct);
        var path = Path.Combine(pasta, $"inter_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    private string GarantirPasta(params string[] partes)
    {
        var segmentos = new[] { _env.ContentRootPath }.Concat(partes).ToArray();
        var caminho = Path.Combine(segmentos);
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    private string LocalizarModeloOnnx()
    {
        const string nome = "ultraface.onnx";
        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models", nome),
            Path.Combine(_env.ContentRootPath,    "Models", nome),
            Path.Combine(_env.ContentRootPath, "..",
                "SimuladorMegaHair.Infrastructure", "Models", nome),
        };

        foreach (var c in candidatos)
        {
            var norm = Path.GetFullPath(c);
            if (File.Exists(norm)) return norm;
        }

        throw new FileNotFoundException($"'{nome}' não encontrado.");
    }

    private HttpRequestMessage CriarRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
        return req;
    }

    private string ConverterParaBase64Sincrono(string caminho)
    {
        if (_base64Cache.TryGetValue(caminho, out var cached))
            return cached;

        var bytes = File.ReadAllBytes(caminho);
        var ext = Path.GetExtension(caminho).TrimStart('.').ToLowerInvariant();

        var mime = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            _ => "image/png"
        };

        var result = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        _base64Cache[caminho] = result;
        return result;
    }

    private static async Task<string> ConverterParaBase64Async(
        string caminho, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(caminho, ct);
        var ext = Path.GetExtension(caminho).TrimStart('.').ToLowerInvariant();

        var mime = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            _ => "image/png"
        };

        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private void LimparArquivosTemp(params string[] caminhos)
    {
        foreach (var c in caminhos)
        {
            try { if (File.Exists(c)) File.Delete(c); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não deletou temp: {Path}", c);
            }
        }
    }

    private static string ExtrairOutput(JsonElement root)
    {
        var output = root.GetProperty("output");
        return output.ValueKind switch
        {
            JsonValueKind.Array => output[0].GetString()!,
            JsonValueKind.String => output.GetString()!,
            _ => throw new InvalidOperationException(
                    $"Output inesperado: {output.ValueKind}")
        };
    }

    private static bool IsNsfwError(Exception ex) =>
        ex.Message.Contains("NSFW", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static string TraduzirCorSimples(string? cor) =>
        cor?.ToLowerInvariant() switch
        {
            "preto" => "black",
            "castanho" => "brown",
            "chocolate" => "dark brown",
            "loiro" => "blonde",
            "mel" => "honey blonde",
            "ruivo" => "auburn",
            "platinado" => "platinum blonde",
            _ => "brown"
        };

    private void ValidarOpcoes()
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiToken))
            throw new InvalidOperationException(
                "Replicate:ApiToken não configurada.");
    }
}