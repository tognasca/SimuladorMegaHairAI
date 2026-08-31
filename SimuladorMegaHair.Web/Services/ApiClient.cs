using System.Net.Http.Json;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Models;

namespace SimuladorMegaHair.Web.Services;

/// <summary>
/// Cliente HTTP para o backend (SimuladorMegaHair.Api), espelhando
/// exatamente o mesmo contrato usado pelo app MAUI (ApiService.cs) —
/// para garantir que web e app nativo se comportem de forma idêntica
/// contra o mesmo servidor.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Envia os bytes de uma foto (ex: capturada pela câmera do
    /// navegador) e retorna o caminho salvo no servidor.
    /// </summary>
    public async Task<string> UploadFotoAsync(byte[] bytes, string nomeArquivo)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(content, "file", nomeArquivo);

        var response = await _http.PostAsync("api/simulacoes/upload", form);
        response.EnsureSuccessStatusCode();

        var caminho = await response.Content.ReadAsStringAsync();
        return caminho.Trim('"');
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/simulacoes", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>();
    }

    public async Task<SimulacaoResponse?> AjustarVolumeAsync(Guid simulacaoId, AjustarVolumeRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/simulacoes/{simulacaoId}/volume", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>();
    }

    public async Task<List<SimulacaoResponse>> GetHistoricoAsync(string? fotoOriginalPath = null)
    {
        var url = "api/simulacoes/historico";
        if (!string.IsNullOrWhiteSpace(fotoOriginalPath))
            url += $"?fotoOriginalPath={Uri.EscapeDataString(fotoOriginalPath)}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SimulacaoResponse>>() ?? new();
    }

    public async Task<List<ClienteResponse>> BuscarClientesAsync(string? busca = null)
    {
        var url = "api/clientes";
        if (!string.IsNullOrWhiteSpace(busca))
            url += $"?busca={Uri.EscapeDataString(busca)}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ClienteResponse>>() ?? new();
    }

    public async Task<ClienteDetalheResponse?> ObterClienteAsync(Guid id)
    {
        var response = await _http.GetAsync($"api/clientes/{id}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClienteDetalheResponse>();
    }

    public async Task<ClienteResponse?> CriarClienteAsync(CriarClienteRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/clientes", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClienteResponse>();
    }

    public async Task<ClienteResponse?> AtualizarClienteAsync(Guid id, AtualizarClienteRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/clientes/{id}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClienteResponse>();
    }

    public async Task<List<CatalogoItem>> GetCatalogoAsync(
        string? cor = null, string? comprimento = null, string? tipoCabelo = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(cor)) query.Add($"cor={Uri.EscapeDataString(cor)}");
        if (!string.IsNullOrWhiteSpace(comprimento)) query.Add($"comprimento={Uri.EscapeDataString(comprimento)}");
        if (!string.IsNullOrWhiteSpace(tipoCabelo)) query.Add($"tipoCabelo={Uri.EscapeDataString(tipoCabelo)}");

        var url = "api/catalogo" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CatalogoItem>>() ?? new();
    }
}
