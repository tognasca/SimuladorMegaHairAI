using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Models;

namespace SimuladorMegaHair.Web.Services;

/// <summary>
/// Cliente HTTP para o backend. O JWT é copiado para cada HttpRequestMessage
/// no momento do envio. Isso evita manter AuthTokenStore scoped dentro de um
/// DelegatingHandler reutilizado pelo IHttpClientFactory entre circuitos.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly AuthTokenStore _tokenStore;

    public ApiClient(HttpClient http, AuthTokenStore tokenStore)
    {
        _http = http;
        _tokenStore = tokenStore;
    }

    public Uri? BaseAddress => _http.BaseAddress;

    /// <summary>Login anônimo contra POST /api/auth/login.</summary>
    public async Task<LoginResponse?> LoginAsync(string email, string senha)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = email, Senha = senha });

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<LoginResponse>();
    }

    public async Task<string> UploadFotoAsync(
        byte[] bytes,
        string nomeArquivo,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", nomeArquivo);

        using var request = CriarRequisicao(HttpMethod.Post, "api/simulacoes/upload");
        request.Content = form;
        using var response = await _http.SendAsync(request, cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível enviar a foto. Tente outra imagem.");

        var caminho = await response.Content.ReadAsStringAsync(cancellationToken);
        return caminho.Trim('"');
    }

    public async Task<SimulacaoResponse?> CriarSimulacaoAsync(
        CriarSimulacaoRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CriarRequisicao(HttpMethod.Post, "api/simulacoes");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        await GarantirSucessoAsync(response,
            "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?");
        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>(cancellationToken: cancellationToken);
    }

    public async Task<SimulacaoResponse?> AjustarVolumeAsync(
        Guid simulacaoId,
        AjustarVolumeRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CriarRequisicao(HttpMethod.Post, $"api/simulacoes/{simulacaoId}/volume");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível ajustar o volume da simulação.");
        return await response.Content.ReadFromJsonAsync<SimulacaoResponse>(cancellationToken: cancellationToken);
    }

    public async Task<List<SimulacaoResponse>> GetHistoricoAsync(
        string? fotoOriginalPath = null,
        CancellationToken cancellationToken = default)
    {
        var url = "api/simulacoes/historico";
        if (!string.IsNullOrWhiteSpace(fotoOriginalPath))
            url += $"?fotoOriginalPath={Uri.EscapeDataString(fotoOriginalPath)}";

        using var response = await EnviarAutorizadaAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível carregar o histórico.");
        return await response.Content.ReadFromJsonAsync<List<SimulacaoResponse>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task<List<ClienteResponse>> BuscarClientesAsync(
        string? busca = null,
        CancellationToken cancellationToken = default)
    {
        var url = "api/clientes";
        if (!string.IsNullOrWhiteSpace(busca))
            url += $"?busca={Uri.EscapeDataString(busca)}";

        using var response = await EnviarAutorizadaAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível carregar os clientes.");
        return await response.Content.ReadFromJsonAsync<List<ClienteResponse>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task<ClienteDetalheResponse?> ObterClienteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var response = await EnviarAutorizadaAsync(HttpMethod.Get, $"api/clientes/{id}", cancellationToken: cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível carregar o cliente.");
        return await response.Content.ReadFromJsonAsync<ClienteDetalheResponse>(cancellationToken: cancellationToken);
    }

    public async Task<ClienteResponse?> CriarClienteAsync(
        CriarClienteRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CriarRequisicao(HttpMethod.Post, "api/clientes");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível criar o cliente.");
        return await response.Content.ReadFromJsonAsync<ClienteResponse>(cancellationToken: cancellationToken);
    }

    public async Task<ClienteResponse?> AtualizarClienteAsync(
        Guid id,
        AtualizarClienteRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CriarRequisicao(HttpMethod.Put, $"api/clientes/{id}");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível atualizar o cliente.");
        return await response.Content.ReadFromJsonAsync<ClienteResponse>(cancellationToken: cancellationToken);
    }

    public async Task<List<CatalogoItem>> GetCatalogoAsync(
        string? cor = null,
        string? comprimento = null,
        string? tipoCabelo = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(cor)) query.Add($"cor={Uri.EscapeDataString(cor)}");
        if (!string.IsNullOrWhiteSpace(comprimento)) query.Add($"comprimento={Uri.EscapeDataString(comprimento)}");
        if (!string.IsNullOrWhiteSpace(tipoCabelo)) query.Add($"tipoCabelo={Uri.EscapeDataString(tipoCabelo)}");

        var url = "api/catalogo" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        using var response = await EnviarAutorizadaAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        await GarantirSucessoAsync(response, "Não foi possível carregar o catálogo.");
        return await response.Content.ReadFromJsonAsync<List<CatalogoItem>>(cancellationToken: cancellationToken) ?? new();
    }

    private HttpRequestMessage CriarRequisicao(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        // O servidor é a autoridade final sobre expiração/validade do JWT.
        // Não bloqueie o header por uma divergência local de DateTime após
        // restauração do Local Storage; sem o header a API responde 401 antes
        // de poder validar o token real.
        if (!string.IsNullOrWhiteSpace(_tokenStore.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenStore.Token);
        return request;
    }

    private Task<HttpResponseMessage> EnviarAutorizadaAsync(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        var request = CriarRequisicao(method, url);
        request.Content = content;
        return EnviarETransferirPosseAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> EnviarETransferirPosseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
            return await _http.SendAsync(request, cancellationToken);
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
                => "Sua sessão expirou ou não está autenticada. Entre novamente para continuar.",
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
