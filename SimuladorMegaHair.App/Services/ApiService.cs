using System.Net.Http.Json;
using SimuladorMegaHair.App.Models;
using SimuladorMegaHair.Domain.Models;

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

    // Dentro da sua classe ApiService:
    public async Task<SimulacaoResponse?> AjustarVolumeAsync(Guid simulacaoId, AjustarVolumeRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/simulacoes/{simulacaoId}/volume",
                request);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<SimulacaoResponse>();
        }
        catch (Exception ex)
        {
            //Debug.WriteLine($"[ApiService] Erro ao ajustar volume: {ex.Message}");
            throw;
        }
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request)
    {
        
        var response = await _httpClient.PostAsJsonAsync("api/simulacoes", request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[ERRO API] {(int)response.StatusCode} - {body}");
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

    // ═══════════════════════════════════════════════════════════
    //  CLIENTES
    // ═══════════════════════════════════════════════════════════

    public async Task<List<Domain.DTOs.ClienteResponse>> BuscarClientesAsync(string? busca = null)
    {
        var url = "api/clientes";
        if (!string.IsNullOrWhiteSpace(busca))
            url += $"?busca={Uri.EscapeDataString(busca)}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<Domain.DTOs.ClienteResponse>>()
            ?? new List<Domain.DTOs.ClienteResponse>();
    }

    public async Task<Domain.DTOs.ClienteDetalheResponse?> ObterClienteAsync(Guid id)
    {
        var response = await _httpClient.GetAsync($"api/clientes/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Domain.DTOs.ClienteDetalheResponse>();
    }

    public async Task<Domain.DTOs.ClienteResponse?> CriarClienteAsync(Domain.DTOs.CriarClienteRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/clientes", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Domain.DTOs.ClienteResponse>();
    }

    public async Task<Domain.DTOs.ClienteResponse?> AtualizarClienteAsync(Guid id, Domain.DTOs.AtualizarClienteRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/clientes/{id}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Domain.DTOs.ClienteResponse>();
    }

    // ═══════════════════════════════════════════════════════════
    //  CATÁLOGO
    // ═══════════════════════════════════════════════════════════

    public async Task<List<Domain.Entities.CatalogoItem>> GetCatalogoAsync(
        string? cor = null, string? comprimento = null, string? tipoCabelo = null, string? metodo = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(cor)) query.Add($"cor={Uri.EscapeDataString(cor)}");
        if (!string.IsNullOrWhiteSpace(comprimento)) query.Add($"comprimento={Uri.EscapeDataString(comprimento)}");
        if (!string.IsNullOrWhiteSpace(tipoCabelo)) query.Add($"tipoCabelo={Uri.EscapeDataString(tipoCabelo)}");
        if (!string.IsNullOrWhiteSpace(metodo)) query.Add($"metodo={Uri.EscapeDataString(metodo)}");

        var url = "api/catalogo" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<Domain.Entities.CatalogoItem>>()
            ?? new List<Domain.Entities.CatalogoItem>();
    }
}