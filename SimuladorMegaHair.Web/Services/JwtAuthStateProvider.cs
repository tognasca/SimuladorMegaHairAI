using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace SimuladorMegaHair.Web.Services;

/// <summary>
/// Ponte entre o AuthTokenStore (nosso JWT contra a API) e o sistema de
/// autorização padrão do Blazor (&lt;AuthorizeView&gt;, [Authorize],
/// AuthorizeRouteView) — permite usar os componentes idiomáticos do
/// framework em vez de checar "está logado?" manualmente em cada página.
/// </summary>
public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthTokenStore _tokenStore;

    public JwtAuthStateProvider(AuthTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
        _tokenStore.OnChange += NotificarMudanca;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = _tokenStore.EstaAutenticado
            ? new ClaimsIdentity(new[]
              {
                  new Claim(ClaimTypes.Name, _tokenStore.EmailUsuario ?? string.Empty)
              }, authenticationType: "jwt")
            : new ClaimsIdentity(); // anônimo

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private void NotificarMudanca()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
