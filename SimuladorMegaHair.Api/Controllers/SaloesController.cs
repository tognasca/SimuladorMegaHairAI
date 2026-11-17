using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/saloes")]
public sealed class SaloesController(AppDbContext db, IConfiguration config) : ControllerBase
{
    // A central usa a conta do operador da instalação principal.
    private async Task<bool> AdministradorAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(config["CadastroSalao:ChaveInicial"])) return false;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        var admin = config["Saas:AdministradorEmail"] ?? config["Piloto:OperadorEmail"]
            ?? config["Orcamento:AdministradorEmail"];
        if (string.IsNullOrWhiteSpace(admin))
            admin = await db.Usuarios.OrderBy(u => u.CriadoEm).ThenBy(u => u.Id)
                .Select(u => u.Email).FirstOrDefaultAsync(ct);
        return !string.IsNullOrWhiteSpace(admin) &&
            string.Equals(email, admin.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public sealed record Lista(CadastroSalao[] Itens, int Total);

    [HttpGet]
    public async Task<IActionResult> Listar(int pagina = 0, CancellationToken ct = default)
    {
        if (!await AdministradorAsync(ct)) return Forbid();
        if (pagina < 0 || pagina > 100000) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        var total = await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM saas_saloes")
            .SingleAsync(ct);
        var itens = await db.Database.SqlQuery<CadastroSalao>($"""
            SELECT id AS "Id", versao AS "Versao", nome AS "Nome", identificador AS "Identificador",
                responsavel AS "Responsavel", email AS "Email", whatsapp AS "WhatsApp",
                url_publica AS "UrlPublica", megahairpro AS "MegaHairPro"
            FROM saas_saloes ORDER BY megahairpro DESC, nome, id LIMIT 50 OFFSET {pagina * 50}
            """).ToArrayAsync(ct);
        return Ok(new Lista(itens, total));
    }

    [HttpPost]
    public async Task<IActionResult> Criar(CadastroSalao cadastro, CancellationToken ct)
    {
        if (!await AdministradorAsync(ct)) return Forbid();
        cadastro.Normalizar();
        if (!Valido(cadastro)) return ValidationProblem(ModelState);
        var id = Guid.NewGuid();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO saas_saloes (id, nome, identificador, responsavel, email, whatsapp, url_publica)
                VALUES ({id}, {cadastro.Nome}, {cadastro.Identificador}, {cadastro.Responsavel},
                    {cadastro.Email}, {cadastro.WhatsApp}, {cadastro.UrlPublica})
                """, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Já existe um salão com este identificador ou link.");
        }
        return Ok(new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, CadastroSalao cadastro, CancellationToken ct)
    {
        if (!await AdministradorAsync(ct)) return Forbid();
        cadastro.Normalizar();
        if (!Valido(cadastro)) return ValidationProblem(ModelState);
        try
        {
            var alterados = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE saas_saloes SET nome = {cadastro.Nome}, responsavel = {cadastro.Responsavel},
                    email = {cadastro.Email}, whatsapp = {cadastro.WhatsApp},
                    url_publica = {cadastro.UrlPublica}, versao = versao + 1, atualizado = now()
                WHERE id = {id} AND versao = {cadastro.Versao} AND identificador = {cadastro.Identificador}
                """, ct);
            if (alterados == 0)
                return Conflict("O cadastro mudou. Recarregue a lista e abra o salão novamente.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Este link já pertence a outro salão.");
        }
        return NoContent();
    }

    private bool Valido(CadastroSalao cadastro)
    {
        var erros = new List<ValidationResult>();
        if (Validator.TryValidateObject(cadastro, new ValidationContext(cadastro), erros, true)) return true;
        foreach (var erro in erros)
            ModelState.AddModelError(erro.MemberNames.FirstOrDefault() ?? "", erro.ErrorMessage ?? "Confira os dados.");
        return false;
    }
}
