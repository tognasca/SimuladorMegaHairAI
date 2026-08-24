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

    public async Task<List<Domain.DTOs.ProviderInfoResponse>> GetProvidersAsync()
    {
        var response = await _httpClient.GetAsync("api/simulacoes/providers");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Domain.DTOs.ProviderInfoResponse>>()
            ?? new List<Domain.DTOs.ProviderInfoResponse>();
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/simulacoes", request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[ERRO API] {(int)response.StatusCode} - {body}");
            // Isso vai imprimir exatamente: "Caminho da foto é obrigatório."
            // ou: {"erro":"Provider 'Local' não está habilitado."}
            // ou: {"type":"...","title":"One or more validation errors occurred.",...}
        }
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