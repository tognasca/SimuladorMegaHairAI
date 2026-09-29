using System.Net;
using SimuladorMegaHair.Web.Services;

namespace SimuladorMegaHair.Tests;

public class ApiClientMensagemTests
{
    [Fact]
    public void PrefereCampoErroDoJson()
    {
        var msg = ApiClient.ExtrairMensagem(
            """{"erro":"Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?"}""",
            HttpStatusCode.UnprocessableEntity,
            "fallback");

        Assert.Equal("Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?", msg);
    }

    [Fact]
    public void EscondeErroTecnico()
    {
        var msg = ApiClient.ExtrairMensagem(
            "System.NullReferenceException: Object reference",
            HttpStatusCode.InternalServerError,
            "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?");

        Assert.DoesNotContain("NullReference", msg);
        Assert.Contains("tentar novamente", msg);
    }
}
