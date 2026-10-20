using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Api.Seguranca;
using SimuladorMegaHair.Infrastructure.Storage;

namespace SimuladorMegaHair.Api.Controllers;

/// <summary>
/// Entrega as fotos (originais e resultados) por URL assinada e com validade.
/// É anônimo de propósito: a assinatura HMAC na própria URL é a autorização
/// (a tag &lt;img&gt; não envia o cabeçalho X-Api-Key). Substitui o antigo
/// UseStaticFiles() sobre wwwroot inteiro, que expunha todas as fotos.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("media")]
public class MediaController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly MediaUrlSigner _signer;

    public MediaController(IWebHostEnvironment env, MediaUrlSigner signer)
    {
        _env = env;
        _signer = signer;
    }

    [HttpGet("{pasta}/{arquivo}")]
    public IActionResult Obter(string pasta, string arquivo, [FromQuery] long exp, [FromQuery] string? sig)
    {
        if (!CaminhosSeguros.TryNormalizar($"{pasta}/{arquivo}", out var p, out var a))
            return NotFound();

        if (!_signer.Validar(p, a, exp, sig))
            return StatusCode(StatusCodes.Status403Forbidden);

        var abs = Path.Combine(_env.ContentRootPath, "wwwroot", p, a);
        if (!System.IO.File.Exists(abs))
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(abs, TipoConteudo(a));
    }

    private static string TipoConteudo(string arquivo) =>
        Path.GetExtension(arquivo).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
