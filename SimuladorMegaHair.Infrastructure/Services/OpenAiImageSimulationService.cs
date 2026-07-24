using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SimuladorMegaHair.Infrastructure.Services;

public class OpenAiImageSimulationService : IImageSimulationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public OpenAiImageSimulationService(
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
        var apiKey = _configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey não configurada.");

        var prompt = PromptBuilder.Build(comprimento, cor, tipoCabelo, metodoMegaHair);

        var absoluteImagePath = Path.IsPathRooted(imagemOriginalPath)
            ? imagemOriginalPath
            : Path.Combine(_env.ContentRootPath, imagemOriginalPath);

        if (!File.Exists(absoluteImagePath))
            throw new FileNotFoundException("Imagem original não encontrada.", absoluteImagePath);

        // ═══════════════════════════════════════════════════
        // 1. DETECTA O ROSTO
        // ═══════════════════════════════════════════════════
        var modelPath = LocalizarModeloOnnx();

        FaceBox? rosto = null;
        try
        {
            using var detector = new FaceDetector(modelPath);
            rosto = detector.DetectarRosto(absoluteImagePath);

            if (rosto != null)
                Console.WriteLine($"[✓] Rosto detectado: {rosto.Confidence:P0}");
            else
                Console.WriteLine("[⚠] Nenhum rosto detectado — usando fallback");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[✗] Erro na detecção facial: {ex.Message}");
        }

        // ═══════════════════════════════════════════════════
        // 2. PREPARA IMAGEM QUADRADA (obrigatório no DALL-E 2)
        // ═══════════════════════════════════════════════════
        var tempFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "temp");
        var imagemPreparada = await ImagePreparer.PrepararParaOpenAiAsync(
            absoluteImagePath, tempFolder, 1024);

        // ═══════════════════════════════════════════════════
        // 3. GERA MÁSCARA (invertida para DALL-E 2)
        // ═══════════════════════════════════════════════════
        var masksFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "masks");
        var maskPath = await HairMaskGenerator.GerarMascaraCabeloAsync(
            imagemPreparada,
            masksFolder,
            rosto,
            cancellationToken);

        // ═══════════════════════════════════════════════════
        // 4. ENVIA PARA OPENAI (DALL-E 2)
        // ═══════════════════════════════════════════════════
        await using var imageStream = File.OpenRead(imagemPreparada);
        await using var maskStream = File.OpenRead(maskPath);

        using var form = new MultipartFormDataContent();

        form.Add(new StringContent("dall-e-2"), "model");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("1024x1024"), "size");
        form.Add(new StringContent("1"), "n");
        // ❌ NÃO envie "response_format" - o DALL-E 2 não aceita esse parâmetro no edits

        var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", "image.png");

        var maskContent = new StreamContent(maskStream);
        maskContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(maskContent, "mask", "mask.png");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/edits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = form;

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Erro na API OpenAI: {response.StatusCode} — {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        var dataElement = document.RootElement.GetProperty("data")[0];

        // ═══════════════════════════════════════════════════
        // 5. BAIXA A IMAGEM (DALL-E 2 retorna URL, não base64)
        // ═══════════════════════════════════════════════════
        byte[] bytes;

        if (dataElement.TryGetProperty("url", out var urlProp))
        {
            var imageUrl = urlProp.GetString();
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new Exception("URL da imagem não retornada pela OpenAI.");

            Console.WriteLine($"[✓] Imagem gerada: {imageUrl}");

            // Baixa a imagem da URL retornada
            bytes = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        }
        else if (dataElement.TryGetProperty("b64_json", out var b64Prop))
        {
            var b64 = b64Prop.GetString();
            if (string.IsNullOrWhiteSpace(b64))
                throw new Exception("Imagem base64 não retornada pela OpenAI.");

            bytes = Convert.FromBase64String(b64);
        }
        else
        {
            throw new Exception("Resposta da OpenAI não contém 'url' nem 'b64_json'.");
        }

        // ═══════════════════════════════════════════════════
        // 6. SALVA O RESULTADO
        // ═══════════════════════════════════════════════════
        var outputFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "resultados");
        Directory.CreateDirectory(outputFolder);

        var fileName = $"{Guid.NewGuid()}.png";
        var outputPath = Path.Combine(outputFolder, fileName);

        await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken);

        // Limpar arquivos temporários
        try { File.Delete(maskPath); } catch { /* ignore */ }
        try { File.Delete(imagemPreparada); } catch { /* ignore */ }

        return $"resultados/{fileName}";
    }

    private string LocalizarModeloOnnx()
    {
        const string nomeModelo = "ultraface.onnx";

        var possiveisCaminhos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models", nomeModelo),
            Path.Combine(_env.ContentRootPath, "Models", nomeModelo),
            Path.Combine(_env.ContentRootPath, "..", "SimuladorMegaHair.Infrastructure", "Models", nomeModelo),
            Path.Combine(_env.ContentRootPath, "wwwroot", "Models", nomeModelo),
            Path.Combine(Directory.GetCurrentDirectory(), "Models", nomeModelo),
        };

        foreach (var caminho in possiveisCaminhos)
        {
            var caminhoNormalizado = Path.GetFullPath(caminho);
            if (File.Exists(caminhoNormalizado))
                return caminhoNormalizado;
        }

        var tentativas = string.Join("\n  - ", possiveisCaminhos.Select(Path.GetFullPath));
        throw new FileNotFoundException(
            $"Modelo {nomeModelo} não encontrado. Locais verificados:\n  - {tentativas}");
    }
}