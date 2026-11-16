using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Api.Services;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/piloto")]
public sealed class PilotoController(
    PainelPiloto painel, CreditosSimulacao creditos, AppDbContext db, IConfiguration config) : ControllerBase
{
    private bool Operador()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        var operador = config["Piloto:OperadorEmail"]?.Trim();
        return !string.IsNullOrWhiteSpace(operador) && string.Equals(email, operador, StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> Ler(CancellationToken ct)
    {
        if (!Operador()) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            creditosAtivos = creditos.Ativo,
            configuracao = await painel.LerAsync(ct),
            indicadores = await painel.IndicadoresAsync(ct)
        });
    }

    [HttpPut]
    public async Task<IActionResult> Salvar(PainelPiloto.Configuracao valores, CancellationToken ct)
    {
        if (!Operador()) return Forbid();
        if (valores.LimiteDiario is < 0 or > 10000 || valores.Preco is < 1 or > 1000
            || valores.Quantidade is < 1 or > 20 || valores.CustoIa is < 0 or > 1000)
            return BadRequest();
        await painel.SalvarAsync(valores, ct);
        return NoContent();
    }

    [HttpPost("whatsapp/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> RegistrarWhatsapp(Guid id, CancellationToken ct)
    {
        if (await db.Simulacoes.AnyAsync(x => x.Id == id, ct))
            await painel.RegistrarAsync("whatsapp", id, 0, ct);
        return NoContent();
    }
}
