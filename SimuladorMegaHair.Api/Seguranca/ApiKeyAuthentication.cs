using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SimuladorMegaHair.Api.Seguranca;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
}

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    /// <summary>Chave esperada. Vazia = ninguém autentica (falha fechada).</summary>
    public string ChaveEsperada { get; set; } = string.Empty;
}

/// <summary>
/// Autenticação por chave compartilhada no cabeçalho "X-Api-Key".
///
/// É a proteção MÍNIMA da Fase 1: fecha a API para quem não conhece a chave
/// (antes, qualquer aparelho na rede lia clientes/fotos e gerava imagens
/// pagas). NÃO identifica pessoas: todos que têm a chave são "o salão".
/// Login por usuário/perfil é o próximo passo (Fase 2).
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.Header, out var valores))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!ChavesIguais(valores.ToString(), Options.ChaveEsperada))
            return Task.FromResult(AuthenticateResult.Fail("Chave de API inválida."));

        var identidade = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "salao") }, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidade), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Comparação em tempo constante (não vaza, pelo tempo de resposta,
    /// quantos caracteres estavam certos). Compara os SHA-256 para que o
    /// tamanho da chave também não vaze.
    /// </summary>
    public static bool ChavesIguais(string recebida, string esperada)
    {
        if (string.IsNullOrEmpty(esperada) || string.IsNullOrEmpty(recebida))
            return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(recebida));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(esperada));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
