using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;

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
            return BadRequest("Formato não permitido.");

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, cancellationToken);

        return Ok(@$"wwwroot\uploads\{fileName}");
    }

    [HttpPost]
    public async Task<ActionResult<SimulacaoResponse>> Criar(
        [FromBody] CriarSimulacaoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FotoOriginalPath))
            return BadRequest("Caminho da foto é obrigatório.");

        // Verifica se já existe simulação idêntica com a mesma foto (cache)
        var existente = await _dbContext.Simulacoes
            .Where(s => s.FotoOriginalPath == request.FotoOriginalPath
                     && s.Comprimento == request.Comprimento
                     && s.Cor == request.Cor
                     && s.TipoCabelo == request.TipoCabelo
                     && s.MetodoMegaHair == request.MetodoMegaHair)
            .FirstOrDefaultAsync(cancellationToken);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // Se já existe, retorna a mesma sem gastar IA
        if (existente is not null)
        {

        //C:\git\SimuladorMegaHair\SimuladorMegaHair.Api\wwwroot\resultados\2bd06852 - 537a - 4419 - 9544 - f15fd8f32bac.png


            return Ok(new SimulacaoResponse
            {
                Id = existente.Id,
                FotoOriginalUrl = $"{baseUrl}/{existente.FotoOriginalPath}",
                FotoResultadoUrl = $"{baseUrl}/{existente.FotoResultadoPath}",
                ValorEstimado = existente.ValorEstimado,
                Comprimento = existente.Comprimento,
                Cor = existente.Cor,
                TipoCabelo = existente.TipoCabelo,
                MetodoMegaHair = existente.MetodoMegaHair,
                CriadoEm = existente.CriadoEm
            });
        }

        // Se não existe, chama a IA
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

        return Ok(new SimulacaoResponse
        {
            Id = simulacao.Id,
            FotoOriginalUrl = $"{baseUrl}/{simulacao.FotoOriginalPath}",
            FotoResultadoUrl = $"{baseUrl}/{simulacao.FotoResultadoPath}",
            ValorEstimado = simulacao.ValorEstimado,
            Comprimento = simulacao.Comprimento,
            Cor = simulacao.Cor,
            TipoCabelo = simulacao.TipoCabelo,
            MetodoMegaHair = simulacao.MetodoMegaHair,
            CriadoEm = simulacao.CriadoEm
        });
    }

    [HttpGet("historico")]
    public async Task<ActionResult<List<SimulacaoResponse>>> ObterHistorico(
        [FromQuery] string? fotoOriginalPath,
        CancellationToken cancellationToken)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var query = _dbContext.Simulacoes.AsQueryable();

        // Se passar a foto, filtra só simulações daquela foto
        if (!string.IsNullOrWhiteSpace(fotoOriginalPath))
            query = query.Where(s => s.FotoOriginalPath == fotoOriginalPath);

        var historico = await query
            .OrderByDescending(s => s.CriadoEm)
            .Take(20)
            .Select(s => new SimulacaoResponse
            {
                Id = s.Id,
                FotoOriginalUrl = $"{baseUrl}/{s.FotoOriginalPath}",
                FotoResultadoUrl = $"{baseUrl}/{s.FotoResultadoPath}",
                ValorEstimado = s.ValorEstimado,
                Comprimento = s.Comprimento,
                Cor = s.Cor,
                TipoCabelo = s.TipoCabelo,
                MetodoMegaHair = s.MetodoMegaHair,
                CriadoEm = s.CriadoEm
            })
            .ToListAsync(cancellationToken);

        return Ok(historico);
    }
}