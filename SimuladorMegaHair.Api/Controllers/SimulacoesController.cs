using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimulacoesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IImageSimulationService _imageSimulationService;
    private readonly IOrcamentoService _orcamentoService;
    private readonly IWebHostEnvironment _env;

    public SimulacoesController(
        AppDbContext dbContext,
        IImageSimulationService imageSimulationService,
        IOrcamentoService orcamentoService,
        IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _imageSimulationService = imageSimulationService;
        _orcamentoService = orcamentoService;
        _env = env;
    }

    // POST api/simulacoes/upload
    [HttpPost("upload")]
    public async Task<ActionResult<string>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Arquivo inválido.");

        var extensoesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extensao = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!extensoesPermitidas.Contains(extensao))
            return BadRequest("Formato não permitido. Use JPG, PNG ou WEBP.");

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, cancellationToken);

        return Ok(@$"wwwroot\uploads\{fileName}");
    }

    // POST api/simulacoes
    [HttpPost]
    public async Task<ActionResult<SimulacaoResponse>> Criar(
        [FromBody] CriarSimulacaoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FotoOriginalPath))
            return BadRequest("Caminho da foto é obrigatório.");

        var resultadoPath = await _imageSimulationService.GerarSimulacaoAsync(
            request.FotoOriginalPath,
            request.Comprimento,
            request.Cor,
            request.TipoCabelo,
            request.MetodoMegaHair,
            cancellationToken);

        var valor = _orcamentoService.Calcular(request.Comprimento, request.MetodoMegaHair);

        var prompt = PromptBuilder.Build(
            request.Comprimento,
            request.Cor,
            request.TipoCabelo,
            request.MetodoMegaHair);

        var simulacao = new Simulacao
        {
            ClienteId = request.ClienteId,
            FotoOriginalPath = request.FotoOriginalPath,
            FotoResultadoPath = resultadoPath,
            Comprimento = request.Comprimento,
            Cor = request.Cor,
            TipoCabelo = request.TipoCabelo,
            MetodoMegaHair = request.MetodoMegaHair,
            PromptUsado = prompt,
            ValorEstimado = valor
        };

        _dbContext.Simulacoes.Add(simulacao);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        return Ok(new SimulacaoResponse
        {
            Id = simulacao.Id,
            FotoOriginalUrl = $"{baseUrl}/{simulacao.FotoOriginalPath}",
            FotoResultadoUrl = $"{baseUrl}/{simulacao.FotoResultadoPath}",
            ValorEstimado = simulacao.ValorEstimado
        });
    }

    // GET api/simulacoes
    [HttpGet]
    public async Task<ActionResult<List<SimulacaoResponse>>> Listar(
        CancellationToken cancellationToken)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var simulacoes = await _dbContext.Simulacoes
            .OrderByDescending(s => s.CriadoEm)
            .Select(s => new SimulacaoResponse
            {
                Id = s.Id,
                FotoOriginalUrl = $"{baseUrl}/{s.FotoOriginalPath}",
                FotoResultadoUrl = $"{baseUrl}/{s.FotoResultadoPath}",
                ValorEstimado = s.ValorEstimado
            })
            .ToListAsync(cancellationToken);

        return Ok(simulacoes);
    }
}