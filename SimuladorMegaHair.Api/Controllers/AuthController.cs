using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext dbContext, IConfiguration config)
    {
        _dbContext = dbContext;
        _config = config;
    }

    /// <summary>
    /// Cria o primeiro usuário do sistema. Só funciona enquanto NÃO existir
    /// nenhum usuário cadastrado — depois disso, retorna 403 e novos
    /// usuários devem ser criados via /api/auth/usuarios (endpoint
    /// autenticado). Isso evita expor um endpoint de criação de conta
    /// aberto permanentemente, e ainda resolve o problema do "ovo e
    /// galinha" de autenticar antes de existir qualquer usuário.
    /// </summary>
    [HttpPost("setup-inicial")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> SetupInicial(
        [FromBody] CriarUsuarioRequest request, CancellationToken ct)
    {
        if (await _dbContext.Usuarios.AnyAsync(ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha) || request.Senha.Length < 8)
            return BadRequest("E-mail obrigatório e senha com no mínimo 8 caracteres.");

        var usuario = new Usuario
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(request.Senha)
        };

        _dbContext.Usuarios.Add(usuario);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(GerarToken(usuario));
    }

    /// <summary>Cria um novo usuário. Requer estar autenticado.</summary>
    [HttpPost("usuarios")]
    [Authorize]
    public async Task<ActionResult> CriarUsuario(
        [FromBody] CriarUsuarioRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha) || request.Senha.Length < 8)
            return BadRequest("E-mail obrigatório e senha com no mínimo 8 caracteres.");

        var emailNormalizado = request.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Usuarios.AnyAsync(u => u.Email == emailNormalizado, ct))
            return Conflict("Já existe um usuário com esse e-mail.");

        _dbContext.Usuarios.Add(new Usuario
        {
            Email = emailNormalizado,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(request.Senha)
        });
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var emailNormalizado = request.Email.Trim().ToLowerInvariant();
        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct);

        // Mensagem genérica de propósito: não revelar se foi o e-mail ou a
        // senha que estava errada (evita enumeração de contas).
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(request.Senha, usuario.SenhaHash))
            return Unauthorized("E-mail ou senha inválidos.");

        return Ok(GerarToken(usuario));
    }

    private LoginResponse GerarToken(Usuario usuario)
    {
        var key = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key não configurado.");
        var issuer = _config["Jwt:Issuer"] ?? "SimuladorMegaHair";
        var audience = _config["Jwt:Audience"] ?? "SimuladorMegaHair.Clients";
        var expiraEm = DateTime.UtcNow.AddHours(12);

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiraEm,
            signingCredentials: credenciais);

        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiraEm = expiraEm
        };
    }
}
