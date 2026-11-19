using SimuladorMegaHair.Api.Services;
using Microsoft.AspNetCore.Authorization;
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
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using SimuladorMegaHair.Api.Seguranca;
using SimuladorMegaHair.Infrastructure.Storage;
using static OpenCvSharp.Stitcher;

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
    private readonly ILogger<SimulacoesController> _logger;

    public SimulacoesController(
        AppDbContext dbContext,
        IImageSimulationService imageService,
        IOrcamentoService orcamentoService,
        IWebHostEnvironment env,
        IOptions<SimulacaoOptions> simOpts,
        ILogger<SimulacoesController> logger)
    {
        _dbContext = dbContext;
        _imageService = imageService;
        _orcamentoService = orcamentoService;
        _env = env;
        _simOpts = simOpts.Value;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════
    //  PROVIDERS DISPONÍVEIS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Retorna os providers de IA disponíveis para o frontend
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
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
    [EnableRateLimiting("UploadPublico")]
    [AllowAnonymous]
    public async Task<ActionResult<string>> Upload(
        IFormFile file,
        [FromServices] MediaUrlSigner signer,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Envie uma foto para começar.");

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest("A foto é grande demais. Use uma imagem de até 10 MB.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!ImageFormatHelper.EhImagemValida(bytes, file.FileName, out var extensao))
            return BadRequest("Use uma foto em JPG, PNG ou WEBP.");

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, bytes, ct);

        // Devolve apenas o caminho e a autorização temporária para gerar.
        // A imagem é entregue separadamente pela rota /media, nunca pelo diretório wwwroot.
        return Ok($"wwwroot/uploads/{fileName}?{signer.Assinar("autorizacao-upload", fileName)}");
    }

   
    // ═══════════════════════════════════════════════════════════
    //  CRIAR SIMULAÇÃO
    // ═══════════════════════════════════════════════════════════

    public sealed record PedidoPrevia(string Metodo, decimal? QuantidadeGramas, string? Origem, Dictionary<string, int>? Extras);

    [HttpPost("previa-orcamento")]
    [AllowAnonymous]
    public async Task<IActionResult> ConsultarPreviaOrcamento(
        [FromBody] PedidoPrevia pedido, [FromServices] OrcamentoLocal pricing, CancellationToken ct) =>
        Ok(await pricing.ConsultarPreviaAsync(pedido.Metodo, pedido.QuantidadeGramas, pedido.Origem, pedido.Extras, ct));

    [HttpGet("metodos-megahairpro")]
    [AllowAnonymous]
    public async Task<ActionResult<string[]>> ListarMetodosMegaHairPro(
        [FromServices] OrcamentoLocal pricing, CancellationToken ct) =>
        Ok((await pricing.LerAsync(ct)).Tabela.Itens.Where(i => i.Grupo == "metodo").Select(i => i.Id).ToArray());

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("Geracao")]
    public async Task<ActionResult<SimulacaoResponse>> Criar(
        [FromBody] CriarSimulacaoRequest request,
        [FromServices] OrcamentoLocal pricing,
        [FromServices] CreditosSimulacao creditos,
        [FromServices] PainelPiloto painel,
        [FromServices] MediaUrlSigner signer,
        CancellationToken ct)
    {


        var partes = (request.FotoOriginalPath ?? "").Split('?', 2);
        if (!CaminhosSeguros.TryNormalizar(partes[0], out var pasta, out var arquivo)
            || pasta != CaminhosSeguros.PastaUploads)
            return BadRequest(new { erro = "Envie uma foto para começar." });

        var equipe = User.Identity?.IsAuthenticated == true;
        if (!equipe)
        {
            var autorizacao = QueryHelpers.ParseQuery(partes.Length == 2 ? partes[1] : "");
            if (!long.TryParse(autorizacao["exp"].ToString(), out var exp)
                || !signer.Validar("autorizacao-upload", arquivo, exp, autorizacao["sig"].ToString()))
                return StatusCode(410, new { erro = "A autorização da foto expirou. Tente gerar outra vez." });
            request.ClienteId = null;
        }
        request.FotoOriginalPath = CaminhosSeguros.Canonico(pasta, arquivo);

        // Provider efetivo: usa o pedido se estiver habilitado; senão cai no DefaultProvider (OpenAI).
        var providerEfetivo = request.Provider;
        if (!ProviderHabilitado(providerEfetivo))
            providerEfetivo = _simOpts.DefaultProvider;
        if (!ProviderHabilitado(providerEfetivo))
            return BadRequest(new
            {
                erro = "Nenhum provedor de simulação está habilitado. Configure OpenAI ou Replicate."
            });
        request.Provider = providerEfetivo;

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var impressao = JsonSerializer.Serialize(new
        {
            request.FotoOriginalPath, request.Comprimento, request.Cor,
            request.TipoCabelo, request.MetodoMegaHair, request.Provider, request.Modo
        });
        var chaveGeracao = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(impressao)));
        await using var bloqueio = await BloqueioGeracao.TentarAsync(_dbContext, chaveGeracao, ct);
        if (bloqueio is null)
            return Conflict(new { erro = "Este visual já está sendo gerado. Aguarde e tente novamente." });

        var precoAtual = await pricing.ConsultarAsync(
            request.MetodoMegaHair, request.QuantidadeGramas, request.OrigemCabelo, request.ExtrasOrcamento, ct);
        if (request.PreviaConfirmada && request.PrecoConfirmado != precoAtual)
            return Conflict(new { erro = "O orçamento foi atualizado. Revise o valor antes de gerar." });

        // ── Cache: mesma foto + mesmos parâmetros + mesmo provider ──
        Simulacao? existente = null;
        if (request.Modo == HairEditMode.Extend && !request.ForcarNovaGeracao)
        {
            existente = await _dbContext.Simulacoes
                .Where(s => s.FotoOriginalPath == request.FotoOriginalPath
                         && s.Comprimento == request.Comprimento
                         && s.Cor == request.Cor
                         && s.TipoCabelo == request.TipoCabelo
                         && s.MetodoMegaHair == request.MetodoMegaHair
                         && s.ProviderUtilizado == request.Provider.ToString())
                .OrderByDescending(s => s.CriadoEm)
                .FirstOrDefaultAsync(ct);
        }
        
        if (existente is not null && ExisteImagemResultado(existente.FotoResultadoPath))
        {
            var responseCache = MontarResponse(existente, baseUrl, signer, veioDoCache: true);
            responseCache.PrecoMegaHairProIndisponivel = request.QuantidadeGramas.HasValue && !precoAtual.HasValue;
            responseCache.ValorEstimado = precoAtual
                ?? _orcamentoService.Calcular(request.Comprimento, request.MetodoMegaHair);
            return Ok(responseCache);
        }
        var reservaId = Guid.NewGuid();
        string? carteira = null;
        var ajustes = await painel.LerAsync(ct);
        if (!equipe && !ajustes.PublicoAtivo)
            return StatusCode(403, new { erro = "Geração pública indisponível. Fale com o salão." });

        if (!equipe)
        {
            carteira = CreditosSimulacao.Carteira(Request.Headers["X-Simulador-Carteira"].ToString());
            if (carteira is null)
                return BadRequest(new { erro = "Abra o simulador novamente para iniciar sua sessão." });
            var reserva = await creditos.ReservarAsync(carteira, reservaId, ct);
            if (reserva == CreditosSimulacao.Reserva.Bloqueada)
                return StatusCode(creditos.Ativo ? 402 : 429, new
                {
                    erro = creditos.Ativo
                        ? "Sua geração gratuita está indisponível. Compre créditos ou fale com o salão."
                        : "Sua geração gratuita está indisponível. Fale com o salão."
                });
        }

        var entregue = false;
        try
        {
            // ── Chama pipeline de IA ────────────────────────────
            SimulacaoResult resultado;

            using var geracaoCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            geracaoCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_simOpts.TempoMaximoGeracaoSegundos, 30, 600)));

            try
            {
                // NOTA (correção da auditoria): antes havia aqui uma chamada extra a
                // _imageService.PipelineKontextAsync(...) (flux-kontext-pro, modelo pago
                // no Replicate) cujo resultado nunca era usado — toda simulação pagava
                // por uma predição descartada. Removida.
                await painel.RegistrarAsync(equipe ? "equipe" : "publico", Guid.NewGuid(), ajustes.CustoIa, ct);
                resultado = await _imageService.GerarSimulacaoAsync(
                    new SimulacaoRequest
                    {
                        ImagemOriginalPath = request.FotoOriginalPath,
                        Comprimento = request.Comprimento,
                        Cor = request.Cor,
                        TipoCabelo = request.TipoCabelo,
                        MetodoMegaHair = request.MetodoMegaHair,
                        Provider = request.Provider,
                        Modo = request.Modo
                    }, geracaoCts.Token);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "Foto não encontrada ao gerar simulação {ClienteId}", request.ClienteId);
                return NotFound(new { erro = "Não encontramos a foto enviada. Envie novamente." });
            }
            catch (FotoInvalidaException ex)
            {
                _logger.LogWarning(ex, "Foto invalida ao gerar simulacao {ClienteId}", request.ClienteId);
                return UnprocessableEntity(new { erro = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Falha ao gerar simulação {ClienteId}", request.ClienteId);
                return UnprocessableEntity(new
                {
                    erro = "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?"
                });
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Causa mais comum aqui: Replicate:ApiToken vazio, ainda com o
                // token antigo (revogado) ou digitado errado no user-secrets/
                // variável de ambiente. Logamos o detalhe completo no servidor,
                // mas devolvemos ao cliente uma mensagem que não vaza o token.
                _logger.LogError(ex,
                    "Falha de autenticação com o provedor de IA ao gerar simulação {ClienteId}",
                    request.ClienteId);

                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    erro = "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?"
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "Falha ao chamar o provedor de IA ao gerar simulação {ClienteId}",
                    request.ClienteId);

                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    erro = "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?"
                });
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout ao gerar simulação {ClienteId}", request.ClienteId);

                return StatusCode(StatusCodes.Status504GatewayTimeout, new
                {
                    erro = "A simulação está demorando mais do que o esperado. Tente novamente."
                });
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Timeout do HttpClient (não foi o usuário/requisição que cancelou).
                _logger.LogError(ex, "Timeout ao gerar simulação {ClienteId}", request.ClienteId);

                return StatusCode(StatusCodes.Status504GatewayTimeout, new
                {
                    erro = "A simulação está demorando mais do que o esperado. Tente novamente."
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao gerar simulação {ClienteId}", request.ClienteId);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    erro = "Não conseguimos gerar a simulação desta vez. Vamos tentar novamente?"
                });
            }

            if (!ExisteImagemResultado(resultado.ImagemResultadoPath))
                return StatusCode(502, new { erro = "A foto gerada ficou indisponível. Tente gerar o visual novamente." });

            // ── Calcula orçamento ───────────────────────────────
            var valor = precoAtual ?? _orcamentoService.Calcular(request.Comprimento, request.MetodoMegaHair);

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
            if (carteira is null)
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            else
            {
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(ct);
                await creditos.ConfirmarAsync(reservaId, ct);
                await _dbContext.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
            }
            entregue = true;

            var response = MontarResponse(simulacao, baseUrl, signer, veioDoCache: false);
            response.Aviso = resultado.Aviso;
            response.PrecoMegaHairProIndisponivel = request.QuantidadeGramas.HasValue && !precoAtual.HasValue;

            return Ok(response);
        }
        finally
        {
            if (!entregue && carteira is not null)
                await creditos.EstornarAsync(reservaId);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  HISTÓRICO
    // ═══════════════════════════════════════════════════════════

    // Reemite URLs temporárias de uma simulação existente sem executar a IA novamente.
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SimulacaoResponse>> ObterSimulacao(
        Guid id, [FromServices] MediaUrlSigner signer, CancellationToken ct)
    {
        var simulacao = await _dbContext.Simulacoes.FindAsync(new object[] { id }, ct);
        if (simulacao is null) return NotFound();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(MontarResponse(simulacao, baseUrl, signer, veioDoCache: true));
    }

    [HttpGet("historico")]
    [Authorize]
    public async Task<ActionResult<List<SimulacaoResponse>>> ObterHistorico(
        [FromServices] MediaUrlSigner signer,
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
            .Select(s => MontarResponse(s, baseUrl, signer, veioDoCache: false))
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
    [Authorize]
    public async Task<ActionResult<SimulacaoResponse>> AjustarVolume(
    Guid id,
    [FromBody] AjustarVolumeRequest request,
    [FromServices] MediaUrlSigner signer,
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
        return Ok(MontarResponse(simulacao, baseUrl, signer, veioDoCache: false));
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
        MediaUrlSigner signer,
        bool veioDoCache) => new()
        {
            Id = simulacao.Id,
            FotoOriginalUrl = UrlMidia(baseUrl, simulacao.FotoOriginalPath, signer),
            FotoResultadoUrl = UrlMidia(baseUrl, simulacao.FotoResultadoPath, signer),
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

    private bool ExisteImagemResultado(string? caminho) =>
        CaminhosSeguros.TryNormalizar(caminho, out var pasta, out var arquivo)
        && pasta == CaminhosSeguros.PastaResultados
        && new FileInfo(Path.Combine(_env.ContentRootPath, "wwwroot", pasta, arquivo)) is { Exists: true, Length: > 0 };

    private static string UrlMidia(string baseUrl, string path, MediaUrlSigner signer) =>
        CaminhosSeguros.TryNormalizar(path, out var pasta, out var arquivo)
            ? $"{baseUrl}/media/{pasta}/{arquivo}?{signer.Assinar(pasta, arquivo)}"
            : string.Empty;
}
