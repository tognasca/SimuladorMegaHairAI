using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SimuladorMegaHair.Api.Seguranca;
using SimuladorMegaHair.Api.Servicos;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using SimuladorMegaHair.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════
//  CONTROLLERS + SWAGGER
// ═══════════════════════════════════════════════════════════

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "MegaHair AI API",
        Version = "v1",
        Description = "API para simulação de mega hair com inteligência artificial."
    });

    // FASE 1: o Swagger em si só fica exposto em Development (abaixo), mas
    // já deixamos documentado como testar com a chave de API.
    options.AddSecurityDefinition(ApiKeyDefaults.Scheme, new()
    {
        Name = ApiKeyDefaults.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Chave de API do salão (cabeçalho X-Api-Key)."
    });
});

// ═══════════════════════════════════════════════════════════
//  CONFIGURAÇÕES (IOptions)
// ═══════════════════════════════════════════════════════════

builder.Services.Configure<SimulacaoOptions>(
    builder.Configuration.GetSection(SimulacaoOptions.Section));

builder.Services.Configure<ReplicateOptions>(
    builder.Configuration.GetSection(ReplicateOptions.Section));

builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection(OpenAIOptions.Section));

// ═══════════════════════════════════════════════════════════
//  SEGURANÇA (FASE 1)
// ═══════════════════════════════════════════════════════════
//
// A chave NUNCA fica em appsettings.json versionado: vem de variável de
// ambiente (MEGAHAIR_API_KEY) ou de "dotnet user-secrets" em desenvolvimento.
// Se estiver vazia, a API recusa TODAS as requisições autenticadas
// (falha fechada) e um aviso é registrado no log ao iniciar.
var apiKey = builder.Configuration["MEGAHAIR_API_KEY"]
             ?? Environment.GetEnvironmentVariable("MEGAHAIR_API_KEY")
             ?? string.Empty;

builder.Services
    .AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, options =>
    {
        options.ChaveEsperada = apiKey;
    });

// Política padrão: qualquer endpoint sem [AllowAnonymous] exige autenticação,
// mesmo que algum controller futuro esqueça o [Authorize].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSingleton(sp =>
{
    var opcoes = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SimulacaoOptions>>().Value;
    // A chave de assinatura deriva da própria API key; se ela mudar, os
    // links assinados antigos expiram naturalmente (o que é desejável).
    return new MediaUrlSigner(apiKey, TimeSpan.FromMinutes(opcoes.ValidadeUrlMidiaMinutos));
});

builder.Services.AddSingleton<GeracaoThrottle>();
builder.Services.AddScoped<ArmazenamentoMidia>();
builder.Services.AddScoped<ExclusaoDadosService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<LimpezaTempService>();

// ═══════════════════════════════════════════════════════════
//  BANCO DE DADOS
// ═══════════════════════════════════════════════════════════

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ═══════════════════════════════════════════════════════════
//  SERVIÇOS DE DOMÍNIO
// ═══════════════════════════════════════════════════════════

builder.Services.AddScoped<IOrcamentoService, OrcamentoService>();

// Pipeline unificado (Local + Replicate + OpenAI)
builder.Services.AddHttpClient<IImageSimulationService, SimulacaoPipelineService>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);
});

// ═══════════════════════════════════════════════════════════
//  CORS
// ═══════════════════════════════════════════════════════════
//
// FASE 1: a política "AllowAll" (qualquer origem, qualquer método, qualquer
// cabeçalho) permitia que QUALQUER site aberto no navegador de um aparelho
// da rede do salão chamasse esta API silenciosamente. Agora só as origens
// listadas em "Cors:OrigensPermitidas" (appsettings) podem chamar a API a
// partir do navegador. Chamadas do app MAUI não passam pelo CORS do
// navegador, então não são afetadas por esta restrição.
var origensPermitidas = builder.Configuration
    .GetSection("Cors:OrigensPermitidas").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("PainelDoSalao", policy =>
    {
        if (origensPermitidas.Length > 0)
        {
            policy.WithOrigins(origensPermitidas)
                  .AllowAnyMethod()
                  .WithHeaders("Content-Type", ApiKeyDefaults.Header);
        }
        // Sem origens configuradas: nenhuma política é aplicada, ou seja,
        // nenhuma chamada de navegador de outra origem é permitida.
    });
});

// ═══════════════════════════════════════════════════════════
//  BUILD & PIPELINE
// ═══════════════════════════════════════════════════════════

var app = builder.Build();

if (string.IsNullOrWhiteSpace(apiKey))
{
    app.Logger.LogWarning(
        "MEGAHAIR_API_KEY não configurada: TODAS as requisições autenticadas " +
        "serão recusadas (falha fechada). Configure a variável de ambiente " +
        "MEGAHAIR_API_KEY ou 'dotnet user-secrets set MEGAHAIR_API_KEY <valor>'.");
}

app.UseExceptionHandler();

// Swagger: só em Development. Em produção o painel ficaria acessível a
// qualquer pessoa na rede, expondo todos os endpoints e seus formatos.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MegaHair AI v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseCors("PainelDoSalao");

app.UseAuthentication();
app.UseAuthorization();

// Só o catálogo (fotos do salão, sem dado pessoal) continua público como
// arquivo estático. Uploads, resultados, máscaras e temporários — que
// contêm fotos de clientes — NÃO são mais servidos como estático; passam
// pelo MediaController, com URL assinada e validade (ver Controllers/
// MediaController.cs).
var pastaCatalogo = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "catalogo");
Directory.CreateDirectory(pastaCatalogo);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(pastaCatalogo),
    RequestPath = "/catalogo"
});

app.MapControllers();

// Migração automática do banco.
// ATENÇÃO: em produção, prefira aplicar migrations por um passo de
// implantação controlado (ex.: "dotnet ef database update" no pipeline de
// deploy), não no início do processo web — uma migration destrutiva (como a
// que já existiu neste projeto) roda sozinha, sem revisão, toda vez que a
// API sobe.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
