using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Infrastructure.Data;

namespace SimuladorMegaHair.Api.Services;

public sealed class OrcamentoLocal(AppDbContext db, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);
    public bool Sincronizado => !string.IsNullOrWhiteSpace(config["Orcamento:ChaveSincronizacao"]);

    public sealed record Item(string Id, string Nome, string Grupo, string Calculo, decimal Valor, int Quantidade = 1);
    public sealed record Tabela(bool MostrarAntes, Item[] Itens);
    public sealed record Estado(Tabela Tabela, DateTimeOffset? Atualizado, bool Sincronizado);
    public sealed record Fonte(string Dados, DateTimeOffset Atualizado);
    public sealed record Previa(bool Ativo, decimal? ValorTotal);
    public sealed record Opcao(string Id, string Nome, string Calculo, decimal? Valor);

    public async Task<Estado> LerAsync(CancellationToken ct)
    {
        var fontes = await db.Database.SqlQueryRaw<Fonte>("""
            SELECT tabela::text AS "Dados", atualizado AS "Atualizado" FROM simulador_orcamento WHERE id = 1
            """).ToListAsync(ct);
        var fonte = fontes.SingleOrDefault();
        var tabela = fonte is null ? null : JsonSerializer.Deserialize<Tabela>(fonte.Dados, JsonWeb);
        return new(tabela ?? new(false, []), fonte?.Atualizado, Sincronizado);
    }

    public static bool Valida(Tabela tabela)
    {
        if (tabela.Itens is null || tabela.Itens.Length > 120) return false;
        var chaves = new HashSet<(string, string)>();
        return tabela.Itens.All(i => i is not null
            && !string.IsNullOrWhiteSpace(i.Id) && i.Id == i.Id.Trim() && i.Id.Length <= 100
            && !string.IsNullOrWhiteSpace(i.Nome) && i.Nome.Length <= 120
            && i.Grupo is "origem" or "metodo" or "incluso" or "opcional" or "consulta"
            && i.Calculo is "fixo" or "grama" or "unidade"
            && i.Quantidade is >= 1 and <= 100
            && (i.Grupo != "origem" || i.Valor > 0)
            && i.Valor is >= 0 and <= 100000
            && chaves.Add((i.Grupo, i.Id.ToUpperInvariant())));
    }

    public Task SalvarAsync(Tabela tabela, CancellationToken ct)
    {
        var dados = JsonSerializer.Serialize(tabela, JsonWeb);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO simulador_orcamento (id, tabela) VALUES (1, {dados}::jsonb)
            ON CONFLICT (id) DO UPDATE SET tabela = EXCLUDED.tabela, atualizado = now()
            WHERE simulador_orcamento.tabela IS DISTINCT FROM EXCLUDED.tabela
            """, ct);
    }

    public async Task<Previa> ConsultarPreviaAsync(string metodo, decimal? gramas, string? origem,
        IReadOnlyDictionary<string, int>? extras, CancellationToken ct)
    {
        var tabela = (await LerAsync(ct)).Tabela;
        return new(tabela.MostrarAntes, tabela.MostrarAntes ? Calcular(tabela, metodo, gramas, origem, extras) : null);
    }

    public async Task<decimal?> ConsultarAsync(string metodo, decimal? gramas, string? origem,
        IReadOnlyDictionary<string, int>? extras, CancellationToken ct) =>
        Calcular((await LerAsync(ct)).Tabela, metodo, gramas, origem, extras);

    private static decimal? Calcular(Tabela tabela, string metodo, decimal? gramas, string? origem,
        IReadOnlyDictionary<string, int>? extras)
    {
        if (gramas is not (> 0 and <= 5000)) return null;
        var cabelo = tabela.Itens.FirstOrDefault(i => i.Grupo == "origem" && Igual(i.Id, origem));
        var aplicacao = tabela.Itens.FirstOrDefault(i => i.Grupo == "metodo" && Igual(i.Id, metodo));
        if (cabelo is null || aplicacao is null) return null;

        decimal Preco(Item i, int? quantidade = null) => i.Valor * (i.Calculo switch
        {
            "grama" => gramas.Value,
            "unidade" => quantidade ?? i.Quantidade,
            _ => 1
        });

        var total = Preco(cabelo) + Preco(aplicacao) + tabela.Itens.Where(i => i.Grupo == "incluso").Sum(i => Preco(i));
        if (extras is not null)
        {
            foreach (var (id, quantidade) in extras)
            {
                if (quantidade == 0) continue;
                var item = tabela.Itens.FirstOrDefault(i => i.Grupo == "opcional" && i.Id == id);
                if (item is null || quantidade < 0 || quantidade > (item.Calculo == "unidade" ? 100 : 1)) return null;
                total += Preco(item, quantidade);
            }
        }
        return decimal.Round(total, 2);
    }

    private static bool Igual(string a, string? b) => string.Equals(a, b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
