using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimulacoesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IImageSimulationService _imageService;
    private readonly IOrcamentoService _orcamentoService;
    private readonly IWebHostEnvironment _env;
    private readonly SimulacaoOptions _simOpts;

    public SimulacoesController(
        AppDbContext dbContext,
        IImageSimulationService imageService,
        IOrcamentoService orcamentoService,
        IWebHostEnvironment env,
        IOptions<SimulacaoOptions> simOpts)
    {
        _dbContext = dbContext;
        _imageService = imageService;
        _orcamentoService = orcamentoService;
        _env = env;
        _simOpts = simOpts.Value;
    }

    // ═══════════════════════════════════════════════════════════
    //  PROVIDERS DISPONÍVEIS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Retorna os providers de IA disponíveis para o frontend
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<List<ProviderInfoResponse>> GetProviders()
    {
        var providers = new List<ProviderInfoResponse>
        {
            new()
            {
                Id         = ImageProvider.Local.ToString(),
                Label      = "Grátis",
                Descricao  = "Simulação básica local, sem custo",
                Gratuito   = true,
                Habilitado = _simOpts.HabilitarProviderLocal,
                Padrao     = _simOpts.DefaultProvider == ImageProvider.Local
            },
            new()
            {
                Id         = ImageProvider.Replicate.ToString(),
                Label      = "Avançado",
                Descricao  = "Flux Fill só no cabelo + freeze do rosto original",
                Gratuito   = false,
                Habilitado = _simOpts.HabilitarProviderReplicate,
                Padrao     = _simOpts.DefaultProvider == ImageProvider.Replicate
            },
            new()
            {
                Id         = ImageProvider.OpenAI.ToString(),
                Label      = "Premium",
                Descricao  = "GPT Image Edit — máxima fidelidade",
                Gratuito   = false,
                Habilitado = _simOpts.HabilitarProviderOpenAI,
                Padrao     = _simOpts.DefaultProvider == ImageProvider.OpenAI
            }
        };

        return Ok(providers.Where(p => p.Habilitado).ToList());
    }

    // ═══════════════════════════════════════════════════════════
    //  UPLOAD
    // ═══════════════════════════════════════════════════════════

    [HttpPost("upload")]
    public async Task<ActionResult<string>> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Arquivo inválido.");

        var extensoesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extensao = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!extensoesPermitidas.Contains(extensao))
            return BadRequest("Formato não permitido.");

        // Limite 10 MB
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest("Arquivo excede 10 MB.");

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, ct);

        // ✅ Padroniza com forward slash (funciona em Windows e Linux)
        return Ok($"wwwroot/uploads/{fileName}");
    }

    // ═══════════════════════════════════════════════════════════
    //  CRIAR SIMULAÇÃO
    // ═══════════════════════════════════════════════════════════

    [HttpPost]
    public async Task<ActionResult<SimulacaoResponse>> Criar(
        [FromBody] CriarSimulacaoRequest request,
        CancellationToken ct)
    {
        // ── Validações ──────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.FotoOriginalPath))
            return BadRequest("Caminho da foto é obrigatório.");

        if (!ProviderHabilitado(request.Provider))
            return BadRequest(new
            {
                erro = $"Provider '{request.Provider}' não está habilitado."
            });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // ── Cache: mesma foto + mesmos parâmetros + mesmo provider ──
        var existente = await _dbContext.Simulacoes
            .Where(s => s.FotoOriginalPath == request.FotoOriginalPath
                     && s.Comprimento == request.Comprimento
                     && s.Cor == request.Cor
                     && s.TipoCabelo == request.TipoCabelo
                     && s.MetodoMegaHair == request.MetodoMegaHair
                     && s.ProviderUtilizado == request.Provider.ToString())
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            return Ok(MontarResponse(existente, baseUrl, veioDoCache: true));
        }

        // ── Chama pipeline de IA ────────────────────────────
        SimulacaoResult resultado;

        try
        {
            resultado = await _imageService.GerarSimulacaoAsync(
                new SimulacaoRequest
                {
                    ImagemOriginalPath = request.FotoOriginalPath,
                    Comprimento = request.Comprimento,
                    Cor = request.Cor,
                    TipoCabelo = request.TipoCabelo,
                    MetodoMegaHair = request.MetodoMegaHair,
                    Provider = request.Provider
                }, ct);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { erro = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { erro = ex.Message });
        }

        // ── Calcula orçamento ───────────────────────────────
        var valor = _orcamentoService.Calcular(
            request.Comprimento, request.MetodoMegaHair);

        // ── Prompt para auditoria ───────────────────────────
        var prompt = request.Provider switch
        {
            //ImageProvider.OpenAI => PromptBuilder.BuildOpenAI(
            //    request.Comprimento, request.Cor,
            //    request.TipoCabelo, request.MetodoMegaHair),

            //ImageProvider.Local => PromptBuilder.BuildLocal(
            //    request.Comprimento, request.Cor, request.TipoCabelo),

            _ => PromptBuilder.BuildInpainting(
                request.Comprimento, request.Cor, request.TipoCabelo, request.MetodoMegaHair, HairEditMode.Extend)
        };

        // ── Persiste ────────────────────────────────────────
        var simulacao = new Simulacao
        {
            ClienteId = request.ClienteId,
            FotoOriginalPath = request.FotoOriginalPath,
            FotoResultadoPath = resultado.ImagemResultadoPath,
            Comprimento = request.Comprimento,
            Cor = request.Cor,
            TipoCabelo = request.TipoCabelo,
            MetodoMegaHair = request.MetodoMegaHair,
            PromptUsado = prompt,
            ValorEstimado = valor,
            ProviderUtilizado = resultado.ProviderUtilizado,
            TempoProcessamentoMs = resultado.TempoProcessamentoMs
        };

        _dbContext.Simulacoes.Add(simulacao);
        await _dbContext.SaveChangesAsync(ct);

        var response = MontarResponse(simulacao, baseUrl, veioDoCache: false);
        response.Aviso = resultado.Aviso;

        return Ok(response);
    }

    // ═══════════════════════════════════════════════════════════
    //  HISTÓRICO
    // ═══════════════════════════════════════════════════════════

    [HttpGet("historico")]
    public async Task<ActionResult<List<SimulacaoResponse>>> ObterHistorico(
        [FromQuery] string? fotoOriginalPath,
        [FromQuery] ImageProvider? provider,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var query = _dbContext.Simulacoes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(fotoOriginalPath))
            query = query.Where(s => s.FotoOriginalPath == fotoOriginalPath);

        if (provider.HasValue)
            query = query.Where(s => s.ProviderUtilizado == provider.Value.ToString());

        take = Math.Clamp(take, 1, 100);

        var historico = await query
            .OrderByDescending(s => s.CriadoEm)
            .Take(take)
            .ToListAsync(ct);

        var response = historico
            .Select(s => MontarResponse(s, baseUrl, veioDoCache: false))
            .ToList();

        return Ok(response);
    }

    // ═══════════════════════════════════════════════════════════
    //  AJUSTAR VOLUME PÓS-GERAÇÃO (GRAMAS MEGA HAIR)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Ajusta o volume/cabelo aparente simulando gramas de mega hair.
    /// Nível 1 = 100g (leve), Nível 4 = 400g (extravolume).
    /// Não reprocessa a IA; usa filtros de imagem inteligentes.
    /// </summary>
    [HttpPost("{id}/volume")]
    public async Task<ActionResult<SimulacaoResponse>> AjustarVolume(
    Guid id,
    [FromBody] AjustarVolumeRequest request,
    CancellationToken ct)
    {
        if (request == null || request.Nivel < 1 || request.Nivel > 4)
        {
            return BadRequest("Nível de volume inválido. Escolha entre 1 e 4.");
        }

        var simulacao = await _dbContext.Simulacoes.FindAsync(new object[] { id }, ct);
        if (simulacao == null)
        {
            return NotFound("Simulação não encontrada.");
        }

        request.ImagemOriginalPath = simulacao.FotoOriginalPath;
        request.ImagemResultadoPath = simulacao.FotoResultadoPath;
        request.Comprimento = simulacao.Comprimento;

        var novoPath = await _imageService.AjustarVolumeAsync(request, ct);

        // Persiste o novo resultado para que fique disponível no histórico
        // do cliente e em futuras consultas dessa simulação.
        simulacao.FotoResultadoPath = novoPath;
        await _dbContext.SaveChangesAsync(ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(MontarResponse(simulacao, baseUrl, veioDoCache: false));
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS PRIVADOS
    // ═══════════════════════════════════════════════════════════

    private bool ProviderHabilitado(ImageProvider provider) => provider switch
    {
        ImageProvider.Local => _simOpts.HabilitarProviderLocal,
        ImageProvider.Replicate => _simOpts.HabilitarProviderReplicate,
        ImageProvider.OpenAI => _simOpts.HabilitarProviderOpenAI,
        _ => false
    };

    // internal: reaproveitado pelo ClientesController para montar o
    // histórico de simulações de um cliente com o mesmo formato.
    internal static SimulacaoResponse MontarResponse(
        Simulacao simulacao,
        string baseUrl,
        bool veioDoCache) => new()
        {
            Id = simulacao.Id,
            FotoOriginalUrl = $"{baseUrl}/{NormalizarPath(simulacao.FotoOriginalPath)}",
            FotoResultadoUrl = $"{baseUrl}/{NormalizarPath(simulacao.FotoResultadoPath)}",
            ValorEstimado = simulacao.ValorEstimado,
            Comprimento = simulacao.Comprimento,
            Cor = simulacao.Cor,
            TipoCabelo = simulacao.TipoCabelo,
            MetodoMegaHair = simulacao.MetodoMegaHair,
            CriadoEm = simulacao.CriadoEm,
            ProviderUtilizado = simulacao.ProviderUtilizado,
            TempoProcessamentoMs = simulacao.TempoProcessamentoMs,
            VeioDoCache = veioDoCache
        };

    /// <summary>
    /// Normaliza path para URL:
    /// - Remove "wwwroot/" (arquivos estáticos servem a partir dele)
    /// - Converte backslash em forward slash
    /// </summary>
    private static string NormalizarPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var normalizado = path.Replace('\\', '/');

        if (normalizado.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            normalizado = normalizado["wwwroot/".Length..];

        return normalizado.TrimStart('/');
    }
}