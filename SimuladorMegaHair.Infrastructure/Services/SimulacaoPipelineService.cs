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
    public async Task<(string url, string? aviso)> PipelineKontextAsync(
    string imagemPath, SimulacaoRequest req, CancellationToken ct)
    {
        var prompt = PromptBuilder.BuildInstrucao(
            req.Comprimento, req.Cor, req.TipoCabelo);

        var body = new
        {
            input = new
            {
                prompt,
                input_image = ConverterBase64(imagemPath),
                output_format = "png",
                safety_tolerance = 2
            }
        };

        var url = $"{ReplicateBaseUrl}/models/black-forest-labs/flux-kontext-pro/predictions";
        var resultado = await EnviarPredicaoAsync(body, ct);
        return (resultado, null);
    }
   
    public async Task<SimulacaoResult> GerarSimulacaoAsync(
        SimulacaoRequest req,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _b64Cache.Clear();

        _logger.LogInformation("═══ SIMULAÇÃO [{Provider}] ═══", req.Provider);

        // 1. Resolve e prepara imagem
        var imagemAbs = ResolverCaminho(req.ImagemOriginalPath);
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var imagemPrep = await ImagePreparer.PrepararParaOpenAiAsync(
            imagemAbs, tempFolder, _rep.ImageSize);

        // 2. Detecta rosto na imagem já recortada (mesmas coordenadas da máscara)
        var rosto = DetectarRostoSeguro(imagemPrep);

        // 3. Gera máscara: camada geométrica (segura) + camada de IA real
        //    (Grounded SAM, via Replicate) intersectadas — ver
        //    HairMaskGenerator para o porquê dessa arquitetura.
        var masksFolder = GarantirPasta("wwwroot", "masks");
        var (maskPath, modoEdit) = await HairMaskGenerator.GerarMascaraInteligenteAsync(
            imagemPrep, masksFolder, rosto, req.Comprimento, _http, _rep, _logger, ct);

        var auditOverlay = await HairMaskAudit.SalvarAsync(
            imagemPrep, maskPath,
            GarantirPasta("wwwroot", "masks", "audit"),
            _logger, ct);

        // 4. Escolhe pipeline
        var (resultadoUrl, aviso) = req.Provider switch
        {
            ImageProvider.Replicate => await PipelineReplicateAsync(imagemPrep, maskPath, req, modoEdit, ct),
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
    //  AJUSTE DE VOLUME (Corrigido para bater com a Interface)
    // ═══════════════════════════════════════════════════════════
    public async Task<string> AjustarVolumeAsync(
    AjustarVolumeRequest req,
    CancellationToken ct = default)
    {
        _logger.LogInformation("[VOLUME] Ajustando volume para nível {Nivel}...", req.Nivel);

        var outputFolder = GarantirPasta("wwwroot", "resultados");
        var masksFolder = GarantirPasta("wwwroot", "masks");

        // Gera (ou re-gera) a máscara de cabelo para a imagem alvo, para que
        // o ganho de volume fique restrito aos fios — nunca afetando rosto
        // ou fundo. Se algo falhar aqui, seguimos sem máscara: o adjuster
        // tem um fallback de segurança (elipse central) mais sutil.
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

    private async Task<(string url, string? aviso)> PipelineReplicateAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        HairEditMode modoEdit,
        CancellationToken ct)
    {
        _logger.LogInformation("[REPLICATE] Iniciando pipeline FLUX Fill (Modo: {Modo})...", modoEdit);

        var fluxUrl = await ExecutarFluxFillAsync(
            imagemPath,
            maskPath,
            req,
            modoEdit,
            ct);

        _logger.LogInformation("[2/2] Freeze — restaurando rosto e fundo...");

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

        return (composto, null);
    }

    private async Task<string> ExecutarFluxFillAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest req,
        HairEditMode modoEdit,
        CancellationToken ct)
    {
        var fallbacks = PromptBuilder.BuildFallbacks(
            req.Comprimento,
            req.Cor,
            req.TipoCabelo,
            req.MetodoMegaHair,
            modoEdit);

        Exception? ultimo = null;

        foreach (var (prompt, negative) in fallbacks)
        {
            try
            {
                Console.WriteLine("==================================================");
                Console.WriteLine($"[PROMPT ENVIADO À IA]: {prompt}");
                Console.WriteLine($"[NEGATIVE PROMPT]: {negative}");
                Console.WriteLine("==================================================");

                var body = new
                {
                    input = new
                    {
                        image = ConverterBase64(imagemPath),
                        mask = ConverterBase64(maskPath),
                        prompt = prompt,
                        num_inference_steps = 30,
                        guidance = 30,
                        num_outputs = 1,
                        output_format = "png",
                        output_quality = 95,
                        seed = _rng.Next(1, 999_999)
                    }
                };

                return await ExecutarComRetryRateLimitAsync(body, ct);
            }
            catch (InvalidOperationException ex) when (IsNsfw(ex))
            {
                ultimo = ex;
                await Task.Delay(2_000, ct);
            }
        }

        throw new InvalidOperationException("Erro de filtro de segurança.", ultimo);
    }

    // ═══════════════════════════════════════════════════════════
    //  REPLICATE HELPERS
    // ═══════════════════════════════════════════════════════════

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
        var url =
           $"https://api.replicate.com/v1/models/" +
           $"{_rep.FluxFillOwner}/" +
           $"{_rep.FluxFillName}/predictions";

        using var req = CriarReplicateRequest(HttpMethod.Post, url);
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