using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Api.Services;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orcamento")]
public sealed class OrcamentoController(OrcamentoLocal orcamento, IConfiguration config) : ControllerBase
{
    private bool Administrador()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        var admin = config["Orcamento:AdministradorEmail"] ?? config["Piloto:OperadorEmail"];
        return !string.IsNullOrWhiteSpace(admin) && string.Equals(email, admin.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> Ler(CancellationToken ct)
    {
        if (!Administrador()) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return Ok(await orcamento.LerAsync(ct));
    }

    [HttpPut]
    public async Task<IActionResult> Salvar(OrcamentoLocal.Tabela tabela, CancellationToken ct)
    {
        if (!Administrador()) return Forbid();
        if (orcamento.Sincronizado) return Conflict();
        if (!OrcamentoLocal.Valida(tabela)) return BadRequest();
        await orcamento.SalvarAsync(tabela, ct);
        return NoContent();
    }

    [HttpGet("opcoes")]
    [AllowAnonymous]
    public async Task<IActionResult> Opcoes(CancellationToken ct)
    {
        var itens = (await orcamento.LerAsync(ct)).Tabela.Itens;
        return Ok(new
        {
            origens = itens.Where(i => i.Grupo == "origem").Select(i => new OrcamentoLocal.Opcao(i.Id, i.Nome, i.Calculo, null)),
            metodos = itens.Where(i => i.Grupo == "metodo").Select(i => i.Id),
            extras = itens.Where(i => i.Grupo == "opcional").Select(i => new OrcamentoLocal.Opcao(i.Id, i.Nome, i.Calculo, i.Valor)),
            sobConsulta = itens.Where(i => i.Grupo == "consulta").Select(i => i.Nome)
        });
    }

    [HttpPut("sincronizar")]
    [AllowAnonymous]
    public async Task<IActionResult> Sincronizar(OrcamentoLocal.Tabela tabela, CancellationToken ct)
    {
        var chave = config["Orcamento:ChaveSincronizacao"];
        if (string.IsNullOrWhiteSpace(chave)) return NotFound();
        var recebida = Request.Headers["X-Orcamento-Chave"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(chave), Encoding.UTF8.GetBytes(recebida)))
            return Unauthorized();
        if (!OrcamentoLocal.Valida(tabela)) return BadRequest();
        await orcamento.SalvarAsync(tabela, ct);
        return NoContent();
    }
}
