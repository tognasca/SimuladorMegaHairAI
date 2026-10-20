using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Infrastructure.Services;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Route("api/foto")]
[ApiExplorerSettings(IgnoreApi = true)]
public class FotoController : ControllerBase
{
    private readonly FotoAvaliadorService _avaliador;

    public FotoController(FotoAvaliadorService avaliador) => _avaliador = avaliador;

    /// Recebe um quadro pequeno (JPEG) da câmera e devolve onde está o rosto (frações 0..1).
    [HttpPost("avaliar")]
    [RequestSizeLimit(2_000_000)]
    public async Task<ActionResult<AvaliacaoRosto>> Avaliar([FromForm] IFormFile frame, CancellationToken ct)
    {
        if (frame is null || frame.Length == 0)
            return BadRequest(new { erro = "Quadro vazio." });

        await using var s = frame.OpenReadStream();
        return Ok(await _avaliador.AvaliarAsync(s, ct));
    }
}
