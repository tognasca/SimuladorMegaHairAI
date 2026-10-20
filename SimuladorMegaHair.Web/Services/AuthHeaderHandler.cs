using System.Net.Http.Headers;

namespace SimuladorMegaHair.Web.Services;

/// <summary>
/// Anexa automaticamente "Authorization: Bearer &lt;token&gt;" em toda
/// requisição feita pelo ApiClient — sem isso, cada método do ApiClient
/// precisaria lembrar de adicionar o header manualmente, e um único
/// esquecimento vira um 401 silencioso e difícil de rastrear.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly AuthTokenStore _tokenStore;

    public AuthHeaderHandler(AuthTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_tokenStore.Token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _tokenStore.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
