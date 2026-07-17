using SimuladorMegaHair.Domain.Entities;
using SimuladorMegaHair.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MegaHair.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogoController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _env;

    public CatalogoController(AppDbContext dbContext, IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _env = env;
    }

    // GET api/catalogo
    [HttpGet]
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

    // POST api/catalogo
    [HttpPost]
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
        var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "catalogo");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(foto.FileName)}";
        var fullPath = Path.Combine(uploadsFolder, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await foto.CopyToAsync(stream, cancellationToken);

        var item = new CatalogoItem
        {
            Titulo = titulo,
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