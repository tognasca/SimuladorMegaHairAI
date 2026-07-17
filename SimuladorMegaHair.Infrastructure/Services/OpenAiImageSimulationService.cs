using System.Net.Http.Headers;
using System.Text.Json;
using SimuladorMegaHair.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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

        await using var imageStream = File.OpenRead(absoluteImagePath);

        using var form = new MultipartFormDataContent();

        form.Add(new StringContent("gpt-image-1"), "model");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("1024x1024"), "size");

        var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(imageContent, "image", Path.GetFileName(absoluteImagePath));

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

        var b64 = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("b64_json")
            .GetString();

        if (string.IsNullOrWhiteSpace(b64))
            throw new Exception("Imagem não retornada pela OpenAI.");

        var bytes = Convert.FromBase64String(b64);

        var outputFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "resultados");
        Directory.CreateDirectory(outputFolder);

        var fileName = $"{Guid.NewGuid()}.png";
        var outputPath = Path.Combine(outputFolder, fileName);

        await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken);

        // Retorna o caminho relativo para a URL pública
        return $"resultados/{fileName}";
    }
}