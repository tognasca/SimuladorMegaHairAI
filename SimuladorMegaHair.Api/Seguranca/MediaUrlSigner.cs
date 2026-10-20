using System.Security.Cryptography;
using System.Text;

namespace SimuladorMegaHair.Api.Seguranca;

/// <summary>
/// Gera e valida URLs assinadas (HMAC-SHA256) com prazo de validade para as
/// fotos. Motivo: as fotos deixaram de ser servidas como arquivos estáticos
/// públicos; a tag &lt;img&gt; do navegador não consegue enviar o cabeçalho
/// X-Api-Key, então a própria URL carrega a prova de autorização — só quem a
/// recebeu da API (autenticada) consegue abrir a imagem, e só até expirar.
/// </summary>
public sealed class MediaUrlSigner
{
    private readonly byte[] _chave;
    private readonly TimeSpan _validade;

    /// <param name="chaveApi">Chave de API; dela se deriva a chave de assinatura.</param>
    public MediaUrlSigner(string chaveApi, TimeSpan validade)
    {
        // Separação de domínio: a chave de assinatura nunca é a própria chave de API.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(chaveApi));
        _chave = hmac.ComputeHash(Encoding.UTF8.GetBytes("media-url-signing-v1"));
        _validade = validade;
    }

    /// <summary>Retorna a query string "exp=...&amp;sig=...".</summary>
    public string Assinar(string pasta, string arquivo, DateTimeOffset? agora = null)
    {
        var exp = (agora ?? DateTimeOffset.UtcNow).Add(_validade).ToUnixTimeSeconds();
        return $"exp={exp}&sig={Calcular(pasta, arquivo, exp)}";
    }

    public bool Validar(string pasta, string arquivo, long exp, string? sig, DateTimeOffset? agora = null)
    {
        if (string.IsNullOrEmpty(sig)) return false;
        if (exp < (agora ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()) return false;

        byte[] recebida;
        try { recebida = DecodificarBase64Url(sig); }
        catch (FormatException) { return false; }

        var esperada = DecodificarBase64Url(Calcular(pasta, arquivo, exp));
        return CryptographicOperations.FixedTimeEquals(recebida, esperada);
    }

    private string Calcular(string pasta, string arquivo, long exp)
    {
        using var hmac = new HMACSHA256(_chave);
        var dados = Encoding.UTF8.GetBytes($"{pasta}/{arquivo}|{exp}");
        return Convert.ToBase64String(hmac.ComputeHash(dados))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] DecodificarBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return Convert.FromBase64String(b64);
    }
}
