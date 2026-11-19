using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Web.Services;

internal static class MidiaSimulacao
{
    internal const string Cliente = "MidiaSimulacao";
    private static readonly Regex Nome = new(
        @"\A[0-9a-f-]{32,36}\.(?:jpe?g|png|webp)\z|\Avolume_[0-9]{3}g_[0-9a-f]{32}\.png\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex Assinatura = new(@"\A[A-Za-z0-9_-]{43}\z", RegexOptions.CultureInvariant);

    internal static string? ParaNavegador(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var origem)
            || (origem.Scheme != Uri.UriSchemeHttps && origem.Scheme != Uri.UriSchemeHttp))
            return null;

        var caminho = origem.AbsolutePath.Split('/');
        // Nunca envia o navegador para um IP local ou para o host interno da API.
        // O endpoint no mesmo domínio do site acessa a API pela rede Docker.
        return caminho.Length == 4 && caminho[1] == "media" && Valido(caminho[2], caminho[3])
            && OrigemAssinada(origem.Query)
            ? $"/midia-simulacao/{caminho[2]}/{caminho[3]}{origem.Query}"
            : null;
    }

    private static bool OrigemAssinada(string query)
    {
        var parametros = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
        return long.TryParse(parametros["exp"].ToString(), out var exp)
            && exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() && Assinatura.IsMatch(parametros["sig"].ToString());
    }

    internal static async Task EntregarAsync(string pasta, string arquivo, long exp, string sig,
        IHttpClientFactory clientes, HttpContext contexto)
    {
        contexto.Response.Headers.CacheControl = "private, no-store";
        contexto.Response.Headers["X-Content-Type-Options"] = "nosniff";
        contexto.Response.Headers["X-Media-Proxy"] = "simulador";
        if (!Valido(pasta, arquivo) || !Assinatura.IsMatch(sig))
        {
            contexto.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(contexto.RequestAborted);
        prazo.CancelAfter(TimeSpan.FromSeconds(18));
        var ct = prazo.Token;
        try
        {
            using var cliente = clientes.CreateClient(Cliente);
            using var resposta = await cliente.GetAsync($"/media/{pasta}/{arquivo}?exp={exp}&sig={sig}",
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resposta.IsSuccessStatusCode)
            {
                contexto.Response.StatusCode = (int)resposta.StatusCode is 403 or 404
                    ? (int)resposta.StatusCode : StatusCodes.Status502BadGateway;
                return;
            }

            var tipo = resposta.Content.Headers.ContentType?.MediaType;
            if (tipo is not ("image/jpeg" or "image/png" or "image/webp"))
            {
                contexto.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            contexto.Response.ContentType = tipo;
            contexto.Response.ContentLength = resposta.Content.Headers.ContentLength;
            await resposta.Content.CopyToAsync(contexto.Response.Body, ct);
        }
        catch (HttpRequestException) when (!contexto.Response.HasStarted)
        {
            contexto.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
        catch (OperationCanceledException) when (!contexto.RequestAborted.IsCancellationRequested && !contexto.Response.HasStarted)
        {
            contexto.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        }
        catch (IOException) when (!contexto.Response.HasStarted)
        {
            contexto.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    private static bool Valido(string pasta, string arquivo) =>
        (pasta is "uploads" or "resultados") && arquivo.Length <= 64 && Nome.IsMatch(arquivo);
}
