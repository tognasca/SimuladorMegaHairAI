using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Services;

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
    [Fact]
    public void PromptPrincipalPreservaIdentidade()
    {
        var prompt = PromptBuilder.BuildInpainting("55 cm", "Castanho Escuro", "Liso", "Fita Adesiva", HairEditMode.Extend);

        Assert.Contains("identical face", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hair", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallbacksNaoInventamPessoaNova()
    {
        var prompts = PromptBuilder.BuildFallbacks("65 cm", "Loiro Claro", "Ondulado", "Queratina", HairEditMode.Extend)
            .Select(p => p.prompt)
            .ToList();

        Assert.All(prompts, p =>
            Assert.True(
                p.Contains("identical face", StringComparison.OrdinalIgnoreCase)
                || p.Contains("preserve identity", StringComparison.OrdinalIgnoreCase)
                || p.Contains("same woman", StringComparison.OrdinalIgnoreCase)));
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
