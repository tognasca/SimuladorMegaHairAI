using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Api.Services;

public sealed class MegaHairProPricingClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<MegaHairProPricingClient> logger)
{
    public async Task<decimal?> ConsultarAsync(
        string comprimento, string cor, string textura, string metodo, CancellationToken ct)
    {
        var settings = configuration.GetSection("MegaHairProPricing");
        if (!Uri.TryCreate(settings["Endpoint"], UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
            return null;

        var regra = settings.GetSection("Mapeamentos").Get<Regra[]>()?.FirstOrDefault(r =>
            Igual(r.Comprimento, comprimento) && Igual(r.Cor, cor)
            && Igual(r.Textura, textura) && Igual(r.Metodo, metodo));

        if (regra is not { ProdutoId: > 0, MetodoId: > 0, QuantidadeGramas: > 0 })
            return null;

        try
        {
            using var response = await http.PostAsJsonAsync(endpoint, new
            {
                regra.ProdutoId,
                regra.MetodoId,
                regra.QuantidadeGramas
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MegaHairPro retornou HTTP {StatusCode} ao consultar o orçamento", response.StatusCode);
                return null;
            }

            var resultado = await response.Content.ReadFromJsonAsync<Resposta>(cancellationToken: ct);
            if (resultado is not { Sucesso: true, Mensagem: not null })
                return null;

            // O endpoint atual entrega o total na mensagem formatada em pt-BR.
            var match = Regex.Match(resultado.Mensagem,
                @"Valor total:[ \t]*\r?\nR\$[ \t]*(?<valor>[0-9.]+,[0-9]{2})[ \t]*\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);

            return match.Success && decimal.TryParse(match.Groups["valor"].Value,
                NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var valor) && valor > 0
                ? valor
                : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Falha ao consultar orçamento no MegaHairPro");
            return null;
        }
    }

    private static bool Igual(string? configurado, string? solicitado) =>
        string.Equals(configurado?.Trim(), solicitado?.Trim(), StringComparison.OrdinalIgnoreCase);

    public sealed record Regra(
        string Comprimento, string Cor, string Textura, string Metodo,
        int ProdutoId, int MetodoId, decimal QuantidadeGramas);

    private sealed record Resposta(bool Sucesso, string? Mensagem);
}
