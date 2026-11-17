using System.ComponentModel.DataAnnotations;
using SimuladorMegaHair.Domain.DTOs;
using Xunit;

namespace SimuladorMegaHair.Tests;

public sealed class CadastroSalaoTests
{
    [Theory]
    [InlineData("https://salao.exemplo.com.br", true)]
    [InlineData("https://salao.exemplo.com.br/", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://salao.exemplo.com.br", false)]
    [InlineData("https://usuario:senha@salao.exemplo.com.br", false)]
    [InlineData("https://salao.exemplo.com.br/captura", false)]
    [InlineData("https://salao.exemplo.com.br/?salon=outro", false)]
    [InlineData("https://salao.exemplo.com.br/#outro", false)]
    public void ValidaLinkDaInstancia(string link, bool esperado) =>
        Assert.Equal(esperado, CadastroSalao.LinkValido(link));

    [Theory]
    [InlineData("outro-salao", true)]
    [InlineData("../tati", false)]
    [InlineData("salao/outro", false)]
    [InlineData("Outro Salão", false)]
    [InlineData("salao--outro", false)]
    public void ValidaIdentificadorDeInstancia(string identificador, bool esperado)
    {
        var cadastro = new CadastroSalao { Nome = "Salão", Identificador = identificador };
        Assert.Equal(esperado, Validator.TryValidateObject(cadastro,
            new ValidationContext(cadastro), new List<ValidationResult>(), true));
    }

    [Fact]
    public void NormalizaLinkParaImpedirDuplicacaoPorCaixaOuBarra()
    {
        var cadastro = new CadastroSalao
        {
            Nome = " Salão ", Identificador = "salao",
            UrlPublica = " https://SALAO.exemplo.com.br/ ", Email = " ADMIN@EXEMPLO.COM "
        };
        cadastro.Normalizar();
        Assert.Equal("https://salao.exemplo.com.br", cadastro.UrlPublica);
        Assert.Equal("admin@exemplo.com", cadastro.Email);
        Assert.Equal("Salão", cadastro.Nome);
    }
}
