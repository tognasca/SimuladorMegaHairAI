using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    public Uri? BaseAddress => _http.BaseAddress;

    /// <summary>
    /// Login contra a API (POST /api/auth/login). Não precisa de token
    /// prévio — o endpoint é anônimo por design.
    /// </summary>
    public async Task<LoginResponse?> LoginAsync(string email, string senha)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = email, Senha = senha });

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<LoginResponse>();
    }

    /// <summary>
    /// Envia os bytes de uma foto (ex: capturada pela câmera do
    /// navegador) e retorna o caminho salvo no servidor.
    /// </summary>
    public async Task<string> UploadFotoAsync(byte[] bytes, string nomeArquivo, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(content, "file", nomeArquivo);

        var response = await _http.PostAsync("api/simulacoes/upload", form, cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível enviar a foto. Tente outra imagem.");

        var caminho = await response.Content.ReadAsStringAsync();
        return caminho.Trim('"');
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(CriarSimulacaoRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/simulacoes", request, cancellationToken);
        await GarantirSucessoAsync(response,
            "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?");
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

    private static async Task GarantirSucessoAsync(HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode)
            return;

        var corpo = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(ExtrairMensagem(corpo, response.StatusCode, fallback));
    }

    internal static string ExtrairMensagem(string? corpo, HttpStatusCode status, string fallback)
    {
        var doJson = TentarLerErroJson(corpo);
        if (!string.IsNullOrWhiteSpace(doJson) && !PareceErroTecnico(doJson))
            return doJson.Trim();

        if (!string.IsNullOrWhiteSpace(corpo)
            && corpo.Length < 180
            && !corpo.TrimStart().StartsWith('{')
            && !PareceErroTecnico(corpo))
            return corpo.Trim('"');

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "Sua sessão expirou. Entre novamente para continuar.",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout
                => "A simulação está demorando mais do que o esperado. Tente novamente.",
            _ => fallback
        };
    }

    private static string? TentarLerErroJson(string? corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("erro", out var erro))
                return erro.GetString();
        }
        catch (JsonException)
        {
            // corpo não é JSON
        }

        return null;
    }

    private static bool PareceErroTecnico(string texto)
    {
        return texto.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("Internal Server", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("status code", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("Replicate", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("NullReference", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("failed to fetch", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("name or service", StringComparison.OrdinalIgnoreCase);
    }

    internal static string MensagemAmigavel(Exception ex, string fallback, Uri? apiBaseAddress = null)
    {
        if (ex is TaskCanceledException)
            return $"A API não respondeu a tempo em {apiBaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:5185"}. Confirme se ela está em execução e tente novamente.";

        if (ex is HttpRequestException)
            return $"Não foi possível conectar à API em {apiBaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:5185"}. Inicie o SimuladorMegaHair.Api e tente novamente.";

        var mensagem = ex.Message?.Trim();
        return !string.IsNullOrWhiteSpace(mensagem) && !PareceErroTecnico(mensagem)
            ? mensagem
            : fallback;
    }
}
