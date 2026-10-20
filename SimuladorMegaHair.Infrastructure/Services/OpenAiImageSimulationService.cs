// ⚠️ CÓDIGO MORTO — NÃO É USADO EM PRODUÇÃO.
// Excluído da build em SimuladorMegaHair.Infrastructure.csproj
// (<Compile Remove="Services\OpenAiImageSimulationService.cs" />).
// O pipeline REAL usado pela aplicação é SimulacaoPipelineService.cs
// (provider Replicate, HabilitarProviderOpenAI = false no appsettings).
// Mantido apenas para referência histórica; pode ser removido com segurança.
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
using SimuladorMegaHair.Infrastructure.Configuration;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class OpenAiImageSimulationService : IImageSimulationService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIOptions _opts;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OpenAiImageSimulationService> _logger;

    private const string OpenAIEditsUrl = "https://api.openai.com/v1/images/edits";

    public OpenAiImageSimulationService(
        HttpClient httpClient,
        IOptions<OpenAIOptions> opts,
        IWebHostEnvironment env,
        ILogger<OpenAiImageSimulationService> logger)
    {
        _httpClient = httpClient;
        _opts = opts.Value;
        _env = env;
        _logger = logger;

        ValidarOpcoes();
    }

    // ═══════════════════════════════════════════════════════════
    //  ENTRADA
    // ═══════════════════════════════════════════════════════════

    public async Task<SimulacaoResult> GerarSimulacaoAsync(
        SimulacaoRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("═══ SIMULAÇÃO OPENAI INICIADA ═══");

        // ── 1. Valida e resolve caminho ─────────────────────
        var imagemAbs = ResolverCaminho(request.ImagemOriginalPath);

        // ── 2. Detecta rosto (best-effort) ──────────────────
        var rosto = DetectarRostoSeguro(imagemAbs);

        // ── 3. Prepara imagem quadrada para OpenAI ──────────
        var tempFolder = GarantirPasta("wwwroot", "temp");
        var imagemPreparada = await ImagePreparer.PrepararParaOpenAiAsync(
            imagemAbs, tempFolder, 1024);

        // ── 4. Gera máscara do cabelo ───────────────────────
        var masksFolder = GarantirPasta("wwwroot", "masks");
        var maskPath = await HairMaskGenerator.GerarMascaraCabeloAsync(
            imagemPreparada, masksFolder, rosto, ct);

        try
        {
            // ── 5. Envia para OpenAI e obtém imagem ─────────
            var bytes = await EnviarParaOpenAiAsync(
                imagemPreparada, maskPath, request, ct);

            // ── 6. Salva resultado ──────────────────────────
            var resultPath = await SalvarResultadoAsync(bytes, ct);

            sw.Stop();
            _logger.LogInformation(
                "═══ CONCLUÍDO em {Ms}ms → {Path} ═══",
                sw.ElapsedMilliseconds, resultPath);

            return new SimulacaoResult
            {
                ImagemResultadoPath = resultPath,
                ProviderUtilizado = "OpenAI",
                TempoProcessamentoMs = sw.ElapsedMilliseconds,
                Aviso = null
            };
        }
        finally
        {
            LimparArquivosTemp(maskPath, imagemPreparada);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  OPENAI API
    // ═══════════════════════════════════════════════════════════

    private async Task<byte[]> EnviarParaOpenAiAsync(
        string imagemPath,
        string maskPath,
        SimulacaoRequest request,
        CancellationToken ct)
    {
        var prompt = PromptBuilder.BuildOpenAI(
            request.Comprimento,
            request.Cor,
            request.TipoCabelo,
            request.MetodoMegaHair);

        _logger.LogDebug("Prompt OpenAI: {P}", prompt);

        await using var imageStream = File.OpenRead(imagemPath);
        await using var maskStream = File.OpenRead(maskPath);

        using var form = new MultipartFormDataContent();

        // Parâmetros do modelo
        form.Add(new StringContent(_opts.Model), "model");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent(_opts.Size), "size");
        form.Add(new StringContent("1"), "n");

        // Só envia quality se for gpt-image-1 (DALL-E 2 não suporta)
        if (_opts.Model.Contains("gpt-image", StringComparison.OrdinalIgnoreCase))
            form.Add(new StringContent(_opts.Quality), "quality");

        // Imagem original
        var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", "image.png");

        // Máscara
        var maskContent = new StreamContent(maskStream);
        maskContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(maskContent, "mask", "mask.png");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, OpenAIEditsUrl);
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
        httpRequest.Content = form;

        using var response = await _httpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Erro OpenAI: {response.StatusCode} — {errorBody}",
                null, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);

        return await ExtrairImagemAsync(document.RootElement, ct);
    }

    /// <summary>
    /// Extrai imagem tanto de URL (DALL-E 2) quanto de b64_json (gpt-image-1)
    /// </summary>
    private async Task<byte[]> ExtrairImagemAsync(
        JsonElement root, CancellationToken ct)
    {
        var dataElement = root.GetProperty("data")[0];

        // gpt-image-1 sempre retorna b64_json
        if (dataElement.TryGetProperty("b64_json", out var b64Prop))
        {
            var b64 = b64Prop.GetString();
            if (string.IsNullOrWhiteSpace(b64))
                throw new InvalidOperationException("b64_json vazio.");

            _logger.LogInformation("[✓] Imagem recebida (base64)");
            return Convert.FromBase64String(b64);
        }

        // DALL-E 2 retorna URL
        if (dataElement.TryGetProperty("url", out var urlProp))
        {
            var imageUrl = urlProp.GetString();
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new InvalidOperationException("URL vazia.");

            _logger.LogInformation("[✓] Imagem gerada: {Url}", imageUrl);
            return await _httpClient.GetByteArrayAsync(imageUrl, ct);
        }

        throw new InvalidOperationException(
            "Resposta da OpenAI não contém 'url' nem 'b64_json'.");
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════

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
                : "Rosto não detectado — usando fallback",
                rosto?.Confidence);

            return rosto;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha na detecção — prosseguindo sem ela");
            return null;
        }
    }

    private async Task<string> SalvarResultadoAsync(
        byte[] bytes, CancellationToken ct)
    {
        var pasta = GarantirPasta("wwwroot", "resultados");
        var nome = $"{Guid.NewGuid()}.png";
        var full = Path.Combine(pasta, nome);

        await File.WriteAllBytesAsync(full, bytes, ct);
        return $"resultados/{nome}";
    }

    private string GarantirPasta(params string[] partes)
    {
        var caminho = Path.Combine(
            new[] { _env.ContentRootPath }.Concat(partes).ToArray());
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    private string LocalizarModeloOnnx()
    {
        const string nome = "ultraface.onnx";

        var candidatos = new[]
        {
            Path.Combine(AppContext.BaseDirectory,     "Models", nome),
            Path.Combine(_env.ContentRootPath,         "Models", nome),
            Path.Combine(_env.ContentRootPath, "..",
                "SimuladorMegaHair.Infrastructure",    "Models", nome),
            Path.Combine(_env.ContentRootPath, "wwwroot",  "Models", nome),
            Path.Combine(Directory.GetCurrentDirectory(),  "Models", nome),
        };

        foreach (var c in candidatos)
        {
            var norm = Path.GetFullPath(c);
            if (File.Exists(norm)) return norm;
        }

        var tentativas = string.Join("\n  - ", candidatos.Select(Path.GetFullPath));
        throw new FileNotFoundException(
            $"Modelo '{nome}' não encontrado. Locais verificados:\n  - {tentativas}");
    }

    private void LimparArquivosTemp(params string[] caminhos)
    {
        foreach (var c in caminhos)
        {
            try
            {
                if (File.Exists(c)) File.Delete(c);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível deletar: {Path}", c);
            }
        }
    }

    private void ValidarOpcoes()
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiToken))
            throw new InvalidOperationException("OpenAI:ApiToken não configurada.");

        if (string.IsNullOrWhiteSpace(_opts.Model))
            throw new InvalidOperationException("OpenAI:Model não configurada.");
    }
}