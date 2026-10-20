using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
using SimuladorMegaHair.Infrastructure.Services;

namespace SimuladorMegaHair.Api.Controllers;

/// SOMENTE DESENVOLVIMENTO. Roda a mesma foto em 3 modelos e gera uma página de comparação.
/// ATENÇÃO: cada chamada faz até 3 predições pagas. Para reduzir: ?modelos=kontext-guia,fill-mascara
[ApiController]
[Route("api/dev")]
public class BenchmarkController : ControllerBase
{
    private readonly IImageSimulationService _img;
    private readonly IWebHostEnvironment _env;

    public BenchmarkController(IImageSimulationService img, IWebHostEnvironment env)
        => (_img, _env) = (img, env);

    [HttpPost("benchmark")]
    public async Task<IActionResult> Benchmark([FromBody] CriarSimulacaoRequest request, [FromQuery] string? modelos, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (_img is not SimulacaoPipelineService svc)
            return BadRequest(new { erro = "Serviço de simulação inesperado." });

        var url = await svc.BenchmarkAsync(new SimulacaoRequest
        {
            ImagemOriginalPath = request.FotoOriginalPath,
            Comprimento = request.Comprimento,
            Cor = request.Cor,
            TipoCabelo = request.TipoCabelo,
            MetodoMegaHair = request.MetodoMegaHair,
            Provider = request.Provider,
            Modo = request.Modo
        }, modelos, ct);

        return Ok(new { pagina = $"{Request.Scheme}://{Request.Host}{url}" });
    }
}