using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Api.Seguranca;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using SimuladorMegaHair.Infrastructure.Storage;

namespace SimuladorMegaHair.Api.Controllers;

// FASE 1: exige o cabeçalho X-Api-Key (ver Seguranca/ApiKeyAuthentication.cs)
// em todos os endpoints. Antes, este controller era totalmente anônimo:
// qualquer um na rede gerava simulações pagas e listava o histórico.
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SimulacoesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IImageSimulationService _imageService;
    private readonly IOrcamentoService _orcamentoService;
    private readonly IWebHostEnvironment _env;
    private readonly SimulacaoOptions _simOpts;
    private readonly MediaUrlSigner _urlSigner;
    private readonly GeracaoThrottle _throttle;
    private readonly ILogger<SimulacoesController> _logger;

    public SimulacoesController(
        AppDbContext dbContext,
        IImageSimulationService imageService,
        IOrcamentoService orcamentoService,
        IWebHostEnvironment env,
        IOptions<SimulacaoOptions> simOpts,
        MediaUrlSigner urlSigner,
        GeracaoThrottle throttle,
        ILogger<SimulacoesController> logger)
    {
        _dbContext = dbContext;
        _imageService = imageService;
        _orcamentoService = orcamentoService;
        _env = env;
        _simOpts = simOpts.Value;
        _urlSigner = urlSigner;
        _throttle = throttle;
        _logger = logger;
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

    // FASE 1 — validação por conteúdo, não por nome de arquivo:
    // 1) lê os "magic bytes" para descobrir o formato REAL (o nome do
    //    arquivo é escolhido livremente por quem envia e não prova nada);
    // 2) rejeita resolução fora da faixa esperada de uma selfie/foto de
    //    rosto, evitando decodificar uma imagem enorme na memória;
    // 3) salva com a extensão do formato REAL, nunca a do nome recebido.
    private const int LadoMinimoPx = 128;
    private const int LadoMaximoPx = 8000;
    private const long PixelsMaximos = 40_000_000; // ~40 MP

    [HttpPost("upload")]
    public async Task<ActionResult<string>> Upload(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { erro = "Arquivo inválido." });

        // Limite 10 MB
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { erro = "Arquivo excede 10 MB." });

        InfoImagem? info;
        await using (var streamLeitura = file.OpenReadStream())
        {
            info = await ImagemInspetor.InspecionarAsync(streamLeitura, ct);
        }

        if (info is null)
            return BadRequest(new { erro = "Formato não permitido. Envie uma foto em JPG, PNG ou WEBP." });

        var motivo = ImagemInspetor.MotivoRejeicao(info, LadoMinimoPx, LadoMaximoPx, PixelsMaximos);
        if (motivo is not null)
            return BadRequest(new { erro = motivo });

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", CaminhosSeguros.PastaUploads);
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{info.Extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using (var streamGravacao = file.OpenReadStream())
        await using (var destino = System.IO.File.Create(fullPath))
        {
            await streamGravacao.CopyToAsync(destino, ct);
        }

        return Ok(CaminhosSeguros.Canonico(CaminhosSeguros.PastaUploads, fileName));
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
        // FASE 1 (P04): o caminho recebido do cliente é normalizado e
        // validado contra a lista de nomes que o PRÓPRIO servidor gera; um
        // caminho absoluto ou "../etc/passwd" é rejeitado aqui, antes de
        // chegar perto de qualquer leitura de arquivo. Corrige a leitura
        // arbitrária de arquivo (o valor era usado sem checagem e o
        // conteúdo era enviado à Replicate).
        if (!CaminhosSeguros.TryNormalizar(request.FotoOriginalPath, out var pastaFoto, out var arquivoFoto)
            || pastaFoto != CaminhosSeguros.PastaUploads)
        {
            return BadRequest(new { erro = "Foto inválida. Envie a foto novamente." });
        }

        var fotoOriginalPath = CaminhosSeguros.Canonico(pastaFoto, arquivoFoto);

        if (!ProviderHabilitado(request.Provider))
            return BadRequest(new
            {
                erro = $"Provider '{request.Provider}' não está habilitado."
            });

        // FASE 1: cota diária simples, para não esgotar o crédito de IA em
        // caso de uso indevido ou de bug em algum cliente.
        var desde = DateTime.UtcNow.AddDays(-1);
        var geradasUltimas24h = await _dbContext.Simulacoes.CountAsync(s => s.CriadoEm >= desde, ct);
        if (geradasUltimas24h >= _simOpts.LimiteDiarioGlobal)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { erro = "Limite diário de simulações atingido. Tente novamente mais tarde." });
        }

        // ── Cache: mesma foto + mesmos parâmetros + mesmo provider ──
        var existente = await _dbContext.Simulacoes
            .Where(s => s.FotoOriginalPath == fotoOriginalPath
                     && s.Comprimento == request.Comprimento
                     && s.Cor == request.Cor
                     && s.TipoCabelo == request.TipoCabelo
                     && s.MetodoMegaHair == request.MetodoMegaHair
                     && s.ProviderUtilizado == request.Provider.ToString())
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            return Ok(MontarResponse(existente, _urlSigner, veioDoCache: true));
        }

        // FASE 1: limita quantas gerações rodam ao mesmo tempo, em vez de
        // deixar a requisição empilhar indefinidamente (memória + limites
        // da Replicate).
        using var vaga = _throttle.TentarEntrar();
        if (vaga is null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { erro = "O sistema está processando muitas simulações agora. Tente novamente em instantes." });
        }

        // ── Chama pipeline de IA ────────────────────────────
        // FASE 1 (P05): antes havia AQUI uma primeira chamada a
        // PipelineKontextAsync cujo resultado (variável "wresultado") era
        // descartado — a IA era chamada e cobrada duas vezes por simulação,
        // e a primeira montava um corpo incompatível com o modelo Fill
        // configurado. Removida; só GerarSimulacaoAsync roda.
        // Erros aqui (arquivo não encontrado, Replicate fora do ar, timeout
        // etc.) não são mais capturados aqui: o GlobalExceptionHandler
        // (Program.cs) padroniza a resposta para todos os casos.
        var resultado = await _imageService.GerarSimulacaoAsync(
            new SimulacaoRequest
            {
                ImagemOriginalPath = fotoOriginalPath,
                Comprimento = request.Comprimento,
                Cor = request.Cor,
                TipoCabelo = request.TipoCabelo,
                MetodoMegaHair = request.MetodoMegaHair,
                Provider = request.Provider
            }, ct);

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
            FotoOriginalPath = fotoOriginalPath,
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

        var response = MontarResponse(simulacao, _urlSigner, veioDoCache: false);
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
            .Select(s => MontarResponse(s, _urlSigner, veioDoCache: false))
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

        return Ok(MontarResponse(simulacao, _urlSigner, veioDoCache: false));
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
    //
    // FASE 1: as fotos não são mais servidas por UseStaticFiles() (link
    // público, sem controle); a URL agora aponta para /media/{pasta}/
    // {arquivo} com uma assinatura HMAC e prazo de validade (ver
    // Seguranca/MediaUrlSigner.cs e Controllers/MediaController.cs).
    private SimulacaoResponse MontarResponse(
        Simulacao simulacao,
        MediaUrlSigner urlSigner,
        bool veioDoCache) =>
        MontarResponseEstatico(simulacao, $"{Request.Scheme}://{Request.Host}", urlSigner, veioDoCache);

    // internal static: reaproveitado pelo ClientesController para montar o
    // histórico de simulações de um cliente com o mesmo formato (o
    // ClientesController monta seu próprio baseUrl a partir do Request dele).
    internal static SimulacaoResponse MontarResponseEstatico(
        Simulacao simulacao,
        string baseUrl,
        MediaUrlSigner urlSigner,
        bool veioDoCache) => new()
        {
            Id = simulacao.Id,
            FotoOriginalUrl = UrlAssinada(baseUrl, urlSigner, simulacao.FotoOriginalPath),
            FotoResultadoUrl = UrlAssinada(baseUrl, urlSigner, simulacao.FotoResultadoPath),
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

    // FASE 1: as fotos não são mais servidas por UseStaticFiles() (link
    // público, sem controle); a URL agora aponta para /media/{pasta}/
    // {arquivo} com uma assinatura HMAC e prazo de validade (ver
    // Seguranca/MediaUrlSigner.cs e Controllers/MediaController.cs).
    private static string UrlAssinada(string baseUrl, MediaUrlSigner urlSigner, string? path)
    {
        if (!CaminhosSeguros.TryNormalizar(path, out var pasta, out var arquivo))
            return string.Empty;

        return $"{baseUrl}/media/{pasta}/{arquivo}?{urlSigner.Assinar(pasta, arquivo)}";
    }
}