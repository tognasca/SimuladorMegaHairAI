using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Api.Seguranca;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;

namespace SimuladorMegaHair.Api.Controllers;

// FASE 1: exige X-Api-Key. Antes, este controller era anônimo e expunha
// nome/telefone/e-mail de todos os clientes, além de permitir excluir
// qualquer um sem qualquer verificação.
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly MediaUrlSigner _urlSigner;
    private readonly ExclusaoDadosService _exclusaoDados;

    public ClientesController(
        AppDbContext dbContext, MediaUrlSigner urlSigner, ExclusaoDadosService exclusaoDados)
    {
        _dbContext = dbContext;
        _urlSigner = urlSigner;
        _exclusaoDados = exclusaoDados;
    }

    // ═══════════════════════════════════════════════════════════
    //  LISTAR / BUSCAR CLIENTES
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Lista clientes, com busca opcional por nome, telefone ou e-mail.
    /// Usado na tela "Buscar cliente que já fez simulação".
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ClienteResponse>>> Listar(
        [FromQuery] string? busca,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Clientes.AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            query = query.Where(c =>
                c.Nome.ToLower().Contains(termo) ||
                (c.Telefone != null && c.Telefone.ToLower().Contains(termo)) ||
                (c.Email != null && c.Email.ToLower().Contains(termo)));
        }

        var clientes = await query
            .OrderByDescending(c => c.CriadoEm)
            .Take(take)
            .Select(c => new
            {
                Cliente = c,
                Total = c.Simulacoes.Count,
                Ultima = c.Simulacoes
                    .OrderByDescending(s => s.CriadoEm)
                    .Select(s => (DateTime?)s.CriadoEm)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var resposta = clientes.Select(x => new ClienteResponse
        {
            Id = x.Cliente.Id,
            Nome = x.Cliente.Nome,
            Email = x.Cliente.Email,
            Telefone = x.Cliente.Telefone,
            CriadoEm = x.Cliente.CriadoEm,
            TotalSimulacoes = x.Total,
            UltimaSimulacaoEm = x.Ultima
        }).ToList();

        return Ok(resposta);
    }

    // ═══════════════════════════════════════════════════════════
    //  DETALHE + HISTÓRICO DE SIMULAÇÕES
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Retorna os dados do cliente junto com todo o seu histórico de
    /// simulações (fotos antes/depois, valores, etc).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClienteDetalheResponse>> ObterPorId(
        Guid id, CancellationToken ct)
    {
        var cliente = await _dbContext.Clientes
            .Include(c => c.Simulacoes)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente is null)
            return NotFound(new { erro = "Cliente não encontrado." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var resposta = new ClienteDetalheResponse
        {
            Id = cliente.Id,
            Nome = cliente.Nome,
            Email = cliente.Email,
            Telefone = cliente.Telefone,
            CriadoEm = cliente.CriadoEm,
            Simulacoes = cliente.Simulacoes
                .OrderByDescending(s => s.CriadoEm)
                .Select(s => SimulacoesController.MontarResponseEstatico(s, baseUrl, _urlSigner, veioDoCache: false))
                .ToList()
        };

        return Ok(resposta);
    }

    // ═══════════════════════════════════════════════════════════
    //  CRIAR CLIENTE
    // ═══════════════════════════════════════════════════════════

    [HttpPost]
    public async Task<ActionResult<ClienteResponse>> Criar(
        [FromBody] CriarClienteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            return BadRequest(new { erro = "Nome é obrigatório." });

        var cliente = new Cliente
        {
            Nome = request.Nome.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Telefone = string.IsNullOrWhiteSpace(request.Telefone) ? null : request.Telefone.Trim()
        };

        _dbContext.Clientes.Add(cliente);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(new ClienteResponse
        {
            Id = cliente.Id,
            Nome = cliente.Nome,
            Email = cliente.Email,
            Telefone = cliente.Telefone,
            CriadoEm = cliente.CriadoEm,
            TotalSimulacoes = 0,
            UltimaSimulacaoEm = null
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  ATUALIZAR CLIENTE
    // ═══════════════════════════════════════════════════════════

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClienteResponse>> Atualizar(
        Guid id, [FromBody] AtualizarClienteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            return BadRequest(new { erro = "Nome é obrigatório." });

        var cliente = await _dbContext.Clientes.FindAsync(new object[] { id }, ct);
        if (cliente is null)
            return NotFound(new { erro = "Cliente não encontrado." });

        cliente.Nome = request.Nome.Trim();
        cliente.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        cliente.Telefone = string.IsNullOrWhiteSpace(request.Telefone) ? null : request.Telefone.Trim();

        await _dbContext.SaveChangesAsync(ct);

        var total = await _dbContext.Simulacoes.CountAsync(s => s.ClienteId == cliente.Id, ct);

        return Ok(new ClienteResponse
        {
            Id = cliente.Id,
            Nome = cliente.Nome,
            Email = cliente.Email,
            Telefone = cliente.Telefone,
            CriadoEm = cliente.CriadoEm,
            TotalSimulacoes = total
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  EXCLUIR CLIENTE
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// FASE 1 (P17 — LGPD): excluir um cliente agora apaga também as fotos
    /// dele em disco. Antes, DeleteBehavior.SetNull só desvinculava as
    /// simulações do cliente; nenhum arquivo era removido e as fotos
    /// continuavam acessíveis pela URL.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        var cliente = await _dbContext.Clientes
            .Include(c => c.Simulacoes)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cliente is null)
            return NotFound(new { erro = "Cliente não encontrado." });

        var simulacoes = cliente.Simulacoes.ToList();
        _dbContext.Clientes.Remove(cliente);

        await _exclusaoDados.ExcluirSimulacoesAsync(simulacoes, ct);

        return NoContent();
    }
}
