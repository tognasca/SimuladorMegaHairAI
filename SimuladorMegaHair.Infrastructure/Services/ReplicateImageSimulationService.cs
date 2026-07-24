using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SimuladorMegaHair.Domain.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SimuladorMegaHair.Infrastructure.Services;

public class ReplicateImageSimulationService : IImageSimulationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    private const string ReplicateBaseUrl = "https://api.replicate.com/v1";

    public ReplicateImageSimulationService(
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _env = env;
    }

    public async Task<string> GerarSimulacaoAsync(
        string imagemOriginalPath,
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair,
        CancellationToken cancellationToken = default)
    {
        var apiToken = _configuration["Replicate:ApiToken"]
            ?? throw new InvalidOperationException("Replicate:ApiToken não configurada.");

        var absoluteImagePath = Path.IsPathRooted(imagemOriginalPath)
            ? imagemOriginalPath
            : Path.Combine(_env.ContentRootPath, imagemOriginalPath);

        if (!File.Exists(absoluteImagePath))
            throw new FileNotFoundException("Imagem original não encontrada.", absoluteImagePath);

        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine("[🎬] FLUXO HÍBRIDO INICIADO");
        Console.WriteLine("═══════════════════════════════════════");

        // ═══ Busca versões atualizadas ═══
        Console.WriteLine("[ℹ] Buscando versões dos modelos...");

        // ✅ Modelo de inpainting oficial
        var inpaintingVersion = await ObterVersaoAtualAsync(
            "stability-ai", "stable-diffusion-inpainting", apiToken);

        // ✅ Face swap alternativo (mais popular)
        var faceSwapVersion = await ObterVersaoAtualAsync(
            "cdingram", "face-swap", apiToken);

        Console.WriteLine($"    Inpainting: {inpaintingVersion[..12]}...");
        Console.WriteLine($"    Face swap:  {faceSwapVersion[..12]}...");

        // ═══ Detecta rosto ═══
        var modelPath = LocalizarModeloOnnx();
        FaceBox? rosto = null;
        try
        {
            using var detector = new FaceDetector(modelPath);
            rosto = detector.DetectarRosto(absoluteImagePath);
            Console.WriteLine(rosto != null
                ? $"[✓] Rosto detectado ({rosto.Confidence:P0})"
                : "[⚠] Rosto não detectado");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[✗] {ex.Message}");
        }

        // ═══ Prepara imagem ═══
        var tempFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "temp");

        // ⚠️ Stable Diffusion 1.5 Inpainting funciona melhor em 512x512
        var imagemPreparada = await ImagePreparer.PrepararParaOpenAiAsync(
            absoluteImagePath, tempFolder, 512);

        // ═══ Máscara ═══
        var masksFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "masks");
        var maskPath = await HairMaskGenerator.GerarMascaraCabeloReplicateAsync(
            imagemPreparada, masksFolder, rosto, cancellationToken);

        // ═══ INPAINTING ═══
        Console.WriteLine("[1/2] 🎨 Gerando cabelo novo...");

        var imagemB64 = await ConverterParaBase64Async(imagemPreparada, cancellationToken);
        var mascaraB64 = await ConverterParaBase64Async(maskPath, cancellationToken);
        var promptCabelo = PromptBuilder.Build(comprimento, cor, tipoCabelo, metodoMegaHair);

        var inpaintingRequest = new
        {
            version = inpaintingVersion,
            input = new
            {
                image = imagemB64,
                mask = mascaraB64,
                prompt = promptCabelo,
                negative_prompt = "logo, watermark, text, cartoon, illustration, painting, " +
                                 "deformed, ugly, blurry, distorted, extra hair, bad anatomy",
                num_inference_steps = 50,
                guidance_scale = 7.5
            }
        };

        var cabeloUrl = await ExecutarComRetryAsync(inpaintingRequest, apiToken, cancellationToken);
        Console.WriteLine($"[✓] Cabelo gerado");

        // Baixa intermediária
        var interBytes = await _httpClient.GetByteArrayAsync(cabeloUrl, cancellationToken);
        var interPath = Path.Combine(tempFolder, $"inter_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(interPath, interBytes, cancellationToken);

        // ═══ FACE SWAP ═══
        Console.WriteLine("[2/2] 👤 Aplicando rosto original...");

        var origB64 = await ConverterParaBase64Async(imagemPreparada, cancellationToken);
        var comCabeloB64 = await ConverterParaBase64Async(interPath, cancellationToken);

        var faceSwapRequest = new
        {
            version = faceSwapVersion,
            input = new
            {
                swap_image = origB64,
                input_image = comCabeloB64
            }
        };

        var finalUrl = await ExecutarComRetryAsync(faceSwapRequest, apiToken, cancellationToken);
        Console.WriteLine($"[✓] Face swap aplicado");

        // ═══ Salva final ═══
        var bytesFinais = await _httpClient.GetByteArrayAsync(finalUrl, cancellationToken);

        var outputFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "resultados");
        Directory.CreateDirectory(outputFolder);

        var fileName = $"{Guid.NewGuid()}.png";
        var outputPath = Path.Combine(outputFolder, fileName);
        await File.WriteAllBytesAsync(outputPath, bytesFinais, cancellationToken);

        try { File.Delete(maskPath); } catch { }
        try { File.Delete(imagemPreparada); } catch { }
        try { File.Delete(interPath); } catch { }

        Console.WriteLine("[🎉] CONCLUÍDO!");
        return $"resultados/{fileName}";
    }

    // ═════════════════════════════════════════════════════════
    // MÉTODOS AUXILIARES
    // ═════════════════════════════════════════════════════════

    private async Task<string> ObterVersaoAtualAsync(
    string owner, string name, string apiToken)
    {
        var url = $"{ReplicateBaseUrl}/models/{owner}/{name}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var resp = await _httpClient.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new Exception(
                $"❌ Modelo '{owner}/{name}' não encontrado no Replicate ({resp.StatusCode}). " +
                $"URL: {url}. Resposta: {errorBody}");
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("latest_version", out var latestVersion))
            throw new Exception($"Modelo '{owner}/{name}' encontrado mas sem versão publicada.");

        return latestVersion.GetProperty("id").GetString()!;
    }

    private async Task<string> ExecutarComRetryAsync(
        object requestBody, string apiToken, CancellationToken cancellationToken,
        int maxTentativas = 3)
    {
        Exception? ultimoErro = null;

        for (int i = 1; i <= maxTentativas; i++)
        {
            try
            {
                return await ExecutarReplicateAsync(requestBody, apiToken, cancellationToken);
            }
            catch (Exception ex) when (ex.Message.Contains("NSFW", StringComparison.OrdinalIgnoreCase))
            {
                ultimoErro = ex;
                Console.WriteLine($"[⚠] NSFW ({i}/{maxTentativas}) — retry...");
                if (i < maxTentativas) await Task.Delay(1000, cancellationToken);
            }
        }

        throw new Exception("Sistema de segurança bloqueou 3x. Tente outra foto.", ultimoErro);
    }

    private async Task<string> ExecutarReplicateAsync(
        object requestBody, string apiToken, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var req = new HttpRequestMessage(HttpMethod.Post, $"{ReplicateBaseUrl}/predictions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        req.Content = content;

        var resp = await _httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Erro Replicate: {resp.StatusCode} — {err}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(respJson);
        var predId = doc.RootElement.GetProperty("id").GetString()!;

        return await AguardarProcessamentoAsync(predId, apiToken, cancellationToken);
    }

    private async Task<string> AguardarProcessamentoAsync(
        string predId, string apiToken, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 90; i++)
        {
            await Task.Delay(2000, cancellationToken);

            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{ReplicateBaseUrl}/predictions/{predId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (i % 5 == 0) Console.WriteLine($"    [⏳] {status}");

            switch (status)
            {
                case "succeeded":
                    var output = doc.RootElement.GetProperty("output");
                    return output.ValueKind switch
                    {
                        JsonValueKind.Array => output[0].GetString()!,
                        JsonValueKind.String => output.GetString()!,
                        _ => throw new Exception("Output desconhecido")
                    };

                case "failed":
                case "canceled":
                    var err = doc.RootElement.TryGetProperty("error", out var e)
                        ? e.GetString() : "erro";
                    throw new Exception($"Processamento falhou: {err}");
            }
        }

        throw new TimeoutException("Timeout no Replicate.");
    }

    private static async Task<string> ConverterParaBase64Async(
        string caminho, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(caminho, ct);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private string LocalizarModeloOnnx()
    {
        const string nome = "ultraface.onnx";
        var caminhos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models", nome),
            Path.Combine(_env.ContentRootPath, "Models", nome),
            Path.Combine(_env.ContentRootPath, "..", "SimuladorMegaHair.Infrastructure", "Models", nome),
        };

        foreach (var c in caminhos)
        {
            var norm = Path.GetFullPath(c);
            if (File.Exists(norm)) return norm;
        }

        throw new FileNotFoundException($"{nome} não encontrado");
    }

    public async Task DebugListarModelosFaceSwapAsync()
    {
        var apiToken = _configuration["Replicate:ApiToken"]!;

        var opcoes = new[]
        {
        ("stability-ai", "stable-diffusion-inpainting"),
        ("stability-ai", "sdxl"),
        ("cdingram", "face-swap"),
        ("omniedgeio", "face-swap"),
        ("yan-ops", "face_swap"),
        ("lucataco", "faceswap"),
        ("codeplugtech", "face-swap"),
        ("arielreplicate", "robust_video_matting"),
    };

        foreach (var (owner, name) in opcoes)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{ReplicateBaseUrl}/models/{owner}/{name}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            try
            {
                var resp = await _httpClient.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var versionId = doc.RootElement
                        .GetProperty("latest_version")
                        .GetProperty("id")
                        .GetString();

                    Console.WriteLine($"✅ {owner}/{name} - EXISTE - versão: {versionId}");
                }
                else
                {
                    Console.WriteLine($"❌ {owner}/{name} - {resp.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {owner}/{name} - ERRO: {ex.Message}");
            }
        }
    }
}