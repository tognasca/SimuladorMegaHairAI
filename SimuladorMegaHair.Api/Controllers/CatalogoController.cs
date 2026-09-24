using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Storage;

namespace MegaHair.Api.Controllers;

// FASE 1 (P07): a leitura do catálogo continua pública (é o que a tablet do
// salão mostra para a cliente navegar, sem dado pessoal). Cadastrar um item
// novo agora exige X-Api-Key e valida o arquivo enviado — antes, qualquer
// pessoa na rede podia hospedar QUALQUER arquivo (inclusive .html/.svg) no
// servidor, sem autenticação, sem limite de tamanho e sem checar o conteúdo.
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CatalogoController : ControllerBase
{
    private const long TamanhoMaximoBytes = 10 * 1024 * 1024;
    private const int LadoMinimoPx = 128;
    private const int LadoMaximoPx = 8000;
    private const long PixelsMaximos = 40_000_000;

    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _env;

    public CatalogoController(AppDbContext dbContext, IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _env = env;
    }

    // GET api/catalogo — público: é o catálogo que a cliente navega no salão.
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<CatalogoItem>>> Listar(
        [FromQuery] string? cor,
        [FromQuery] string? comprimento,
        [FromQuery] string? tipoCabelo,
        [FromQuery] string? metodo,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CatalogoItens
            .Where(c => c.Ativo)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(cor))
            query = query.Where(c => c.Cor.ToLower().Contains(cor.ToLower()));

        if (!string.IsNullOrWhiteSpace(comprimento))
            query = query.Where(c => c.Comprimento.ToLower() == comprimento.ToLower());

        if (!string.IsNullOrWhiteSpace(tipoCabelo))
            query = query.Where(c => c.TipoCabelo.ToLower() == tipoCabelo.ToLower());

        if (!string.IsNullOrWhiteSpace(metodo))
            query = query.Where(c => c.MetodoMegaHair.ToLower() == metodo.ToLower());

        var itens = await query
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(cancellationToken);

        return Ok(itens);
    }

    // POST api/catalogo — exige X-Api-Key: só o salão cadastra itens.
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<CatalogoItem>> Adicionar(
        [FromForm] string titulo,
        [FromForm] string comprimento,
        [FromForm] string cor,
        [FromForm] string tipoCabelo,
        [FromForm] string metodoMegaHair,
        [FromForm] decimal precoBase,
        [FromForm] bool autorizadoUsoImagem,
        IFormFile foto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            return BadRequest(new { erro = "Título é obrigatório." });

        if (foto is null || foto.Length == 0)
            return BadRequest(new { erro = "Foto é obrigatória." });

        if (foto.Length > TamanhoMaximoBytes)
            return BadRequest(new { erro = "Arquivo excede 10 MB." });

        InfoImagem? info;
        await using (var streamLeitura = foto.OpenReadStream())
        {
            info = await ImagemInspetor.InspecionarAsync(streamLeitura, cancellationToken);
        }

        if (info is null)
            return BadRequest(new { erro = "Formato não permitido. Envie uma foto em JPG, PNG ou WEBP." });

        var motivo = ImagemInspetor.MotivoRejeicao(info, LadoMinimoPx, LadoMaximoPx, PixelsMaximos);
        if (motivo is not null)
            return BadRequest(new { erro = motivo });

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "catalogo");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{info.Extensao}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using (var streamGravacao = foto.OpenReadStream())
        await using (var destino = System.IO.File.Create(fullPath))
        {
            await streamGravacao.CopyToAsync(destino, cancellationToken);
        }

        var item = new CatalogoItem
        {
            Titulo = titulo.Trim(),
            FotoPath = $"catalogo/{fileName}",
            Comprimento = comprimento,
            Cor = cor,
            TipoCabelo = tipoCabelo,
            MetodoMegaHair = metodoMegaHair,
            PrecoBase = precoBase,
            AutorizadoUsoImagem = autorizadoUsoImagem
        };

        _dbContext.CatalogoItens.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(item);
    }
}
