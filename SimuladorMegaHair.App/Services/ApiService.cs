using System.Net.Http.Json;
using SimuladorMegaHair.App.Models;

namespace SimuladorMegaHair.App.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> UploadFotoAsync(string filePath)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);

        var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        var response = await _httpClient.PostAsync("api/simulacoes/upload", form);
        response.EnsureSuccessStatusCode();

        var caminho = await response.Content.ReadAsStringAsync();
        return caminho.Trim('"');
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/simulacoes", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>();
    }

    public async Task<List<SimulacaoResponse>> GetHistoricoAsync(string? fotoOriginalPath = null)
    {
        var url = "api/simulacoes/historico";
        if (!string.IsNullOrWhiteSpace(fotoOriginalPath))
            url += $"?fotoOriginalPath={Uri.EscapeDataString(fotoOriginalPath)}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<SimulacaoResponse>>()
            ?? new List<SimulacaoResponse>();
    }
}