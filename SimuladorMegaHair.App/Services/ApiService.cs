using SimuladorMegaHair.App.Models;
using System.Net.Http.Json;

namespace SimuladorMegaHair.App.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Envia a foto original para o servidor.
    /// Retorna o caminho relativo salvo no backend.
    /// </summary>
    public async Task<string> UploadFotoAsync(string filePath)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);

        var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        var response = await _httpClient.PostAsync("api/simulacoes/upload", form);
        response.EnsureSuccessStatusCode();

        var caminho = await response.Content.ReadAsStringAsync();

        // Retorna sem aspas
        return caminho.Trim('"');
    }

    /// <summary>
    /// Cria a simulação chamando a IA no backend.
    /// </summary>
    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/simulacoes", request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>();
    }
}