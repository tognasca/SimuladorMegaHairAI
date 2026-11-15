using System.Net.Http.Json;

namespace SimuladorMegaHair.Api.Services;

public sealed class MegaHairProPricingClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<MegaHairProPricingClient> logger)
{
    public async Task<decimal?> ConsultarAsync(string metodo, decimal? quantidadeGramas, CancellationToken ct)
    {
        if (quantidadeGramas is not > 0)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("preco"))
        {
            Content = JsonContent.Create(new { metodo, quantidadeGramas })
        };
        var resposta = await ConsultarApiAsync<Preco>(request, ct);
        return resposta?.ValorTotal is > 0 ? resposta.ValorTotal : null;
    }

    public async Task<string[]> ListarMetodosAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("metodos"));
        return await ConsultarApiAsync<string[]>(request, ct) ?? Array.Empty<string>();
    }

    private Uri Endpoint(string recurso)
    {
        var baseUrl = configuration["MegaHairProPricing:BaseUrl"] ?? "https://megahairpro.tatimegahair.com.br";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), "/api/simulador-megahair/" + recurso);
    }

    private async Task<T?> ConsultarApiAsync<T>(HttpRequestMessage request, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MegaHairPro retornou HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Falha ao consultar o MegaHairPro");
            return null;
        }
    }

    private sealed record Preco(decimal ValorTotal);
}
