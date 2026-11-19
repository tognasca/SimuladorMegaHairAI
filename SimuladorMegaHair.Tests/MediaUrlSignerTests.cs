using Microsoft.AspNetCore.WebUtilities;
using SimuladorMegaHair.Api.Seguranca;
using Xunit;

namespace SimuladorMegaHair.Tests;

public class MediaUrlSignerTests
{
    private const string Pasta = "resultados";
    private const string Arquivo = "01234567890123456789012345678901.png";
    private const string Chave = "chave-de-teste-exclusiva-do-primeiro-salao";
    private static readonly DateTimeOffset Agora = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly MediaUrlSigner Signer = new(Chave, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(-25, true)]
    [InlineData(-27, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void RenovacaoRespeitaJanelaDaAssinatura(int horas, bool aceita)
    {
        var (exp, sig) = Assinar(Agora.AddHours(horas));
        Assert.Equal(aceita, Signer.ValidarRenovacao(Pasta, Arquivo, exp, sig, Agora));
    }

    [Fact]
    public void AssinaturaExpiradaNaoEntregaFotoMesmoDuranteJanelaDeRenovacao()
    {
        var (exp, sig) = Assinar(Agora.AddHours(-3));
        Assert.False(Signer.Validar(Pasta, Arquivo, exp, sig, Agora));
        Assert.True(Signer.ValidarRenovacao(Pasta, Arquivo, exp, sig, Agora));
    }

    [Fact]
    public void RenovacaoRecusaProvaAusenteOuPertencenteAOutroArquivoOuSalao()
    {
        var (exp, sig) = Assinar(Agora);
        var outroSalao = new MediaUrlSigner("chave-de-teste-exclusiva-do-segundo-salao", TimeSpan.FromHours(2));
        Assert.False(Signer.ValidarRenovacao(Pasta, Arquivo, exp, null, Agora));
        Assert.False(Signer.ValidarRenovacao(Pasta, Arquivo, exp, new string('!', 43), Agora));
        Assert.False(Signer.ValidarRenovacao(Pasta, Arquivo, exp - 1, sig, Agora));
        Assert.False(Signer.ValidarRenovacao("uploads", Arquivo, exp, sig, Agora));
        Assert.False(Signer.ValidarRenovacao(Pasta, "outro.png", exp, sig, Agora));
        Assert.False(outroSalao.ValidarRenovacao(Pasta, Arquivo, exp, sig, Agora));
    }

    private static (long Exp, string Sig) Assinar(DateTimeOffset instante)
    {
        var query = QueryHelpers.ParseQuery(Signer.Assinar(Pasta, Arquivo, instante));
        return (long.Parse(query["exp"].ToString()), query["sig"].ToString());
    }
}
