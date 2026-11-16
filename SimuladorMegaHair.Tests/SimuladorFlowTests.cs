using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Services;
using Xunit;

namespace SimuladorMegaHair.Tests;

public class ImageFormatHelperTests
{
    [Fact]
    public void AceitaPngMinimo()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        Assert.True(ImageFormatHelper.EhImagemValida(png, "foto.bin", out var ext));
        Assert.Equal(".png", ext);
    }

    [Fact]
    public void RecusaArquivoAleatorio()
    {
        var lixo = "isso nao e imagem"u8.ToArray();
        Assert.False(ImageFormatHelper.EhImagemValida(lixo, "foto.jpg", out _));
    }
}

public class PromptBuilderTests
{
    [Theory]
    [InlineData(HairEditMode.Extend)]
    [InlineData(HairEditMode.Shorten)]
    [InlineData(HairEditMode.Recolor)]
    public void PromptPrincipalPreservaIdentidade(HairEditMode modo)
    {
        var prompt = PromptBuilder.BuildInpainting("55 cm", "Castanho Escuro", "Liso", "Fita Adesiva", modo);

        Assert.Contains("Edit only the hair inside the supplied mask", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preserve the original face, body, clothing, and background unchanged", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallbacksNaoInventamPessoaNova()
    {
        var prompts = PromptBuilder.BuildFallbacks("65 cm", "Loiro Claro", "Ondulado", "Queratina", HairEditMode.Extend)
            .Select(p => p.prompt)
            .ToList();

        Assert.Equal(3, prompts.Count);
        Assert.All(prompts, p =>
        {
            Assert.Contains("supplied mask", p, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                p.Contains("Preserve the original face", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Do not create or alter any person, face", StringComparison.OrdinalIgnoreCase),
                p);
            Assert.Contains("background", p, StringComparison.OrdinalIgnoreCase);
        });
    }
}

public class ReplicateOptionsTests
{
    [Fact]
    public void TokenPadraoNaoVemPreenchido()
    {
        var opts = new ReplicateOptions();
        Assert.True(string.IsNullOrWhiteSpace(opts.ApiToken));
    }
}

public class OrcamentoServiceTests
{
    [Fact]
    public void CalculaMetodoConhecido()
    {
        var svc = new OrcamentoService();
        Assert.Equal(1200m, svc.Calcular("55 cm", "Fita Adesiva"));
    }
}
