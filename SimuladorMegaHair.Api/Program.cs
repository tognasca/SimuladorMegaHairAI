using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// CAMINHOS DOS ASSETS
// ==========================================================

var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
var catalogPath = Path.Combine(assetsPath, "catalogo");
var manifestPath = Path.Combine(catalogPath, "manifest.json");

// Validação antecipada: falha na inicialização, com mensagem clara,
// em vez de falhar na primeira simulação.
if (!Directory.Exists(catalogPath))
{
    throw new DirectoryNotFoundException(
        $"Pasta do catálogo não encontrada: {catalogPath}");
}

if (!File.Exists(manifestPath))
{
    throw new FileNotFoundException(
        $"Manifesto do catálogo não encontrado: {manifestPath}",
        manifestPath);
}

// ==========================================================
// CONTROLLERS E SWAGGER
// ==========================================================

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

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole somente o token JWT obtido em /api/Auth/login. O Swagger adiciona o prefixo Bearer automaticamente."
    });

// ==========================================================
// CONFIGURAÇÕES
// ==========================================================

builder.Services.Configure<SimulacaoOptions>(
    builder.Configuration.GetSection(SimulacaoOptions.Section));

builder.Services.Configure<ReplicateOptions>(
    builder.Configuration.GetSection(ReplicateOptions.Section));

builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection(OpenAIOptions.Section));

// ==========================================================
// BANCO DE DADOS
// ==========================================================

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ==========================================================
// CATÁLOGO, ZONA DE EDIÇÃO E SEGMENTAÇÃO DE CABELO
// (cada item registrado UMA única vez)
// ==========================================================

builder.Services.AddSingleton(_ => new HairMaskBuilder(catalogPath));
builder.Services.AddScoped<HairZoneGenerator>();

// Versão verificada do modelo schananas/grounded_sam no Replicate.
// Pode ser sobrescrita em Replicate:SegmentVersion (appsettings / variável de ambiente).
const string DefaultSegmentVersion =
    "171d73909aa0cd42bc873d5b82d795eef90ecb24001da095af8708d37548b02a";

builder.Services.AddHttpClient<HairSegmenter>()
    .AddTypedClient((http, sp) =>
        new HairSegmenter(
            http,
            builder.Configuration["Replicate:ApiToken"] ?? string.Empty,
            builder.Configuration["Replicate:SegmentVersion"] ?? DefaultSegmentVersion,
            sp.GetRequiredService<ILogger<HairSegmenter>>()));

// ==========================================================
// SERVIÇOS DE DOMÍNIO E SIMULAÇÃO
// ==========================================================

builder.Services.AddScoped<IOrcamentoService, OrcamentoService>();

// Detecção de rosto em tempo real para a tela de captura (um único detector em memória).
builder.Services.AddSingleton<FotoAvaliadorService>();

builder.Services.AddHttpClient<
    IImageSimulationService,
    SimulacaoPipelineService>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(10);
    });

// ==========================================================
// CORS
// ==========================================================

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ==========================================================
// AUTENTICAÇÃO JWT
// ==========================================================

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key não configurado. Defina Jwt__Key por variável de ambiente " +
        "ou User Secrets, com pelo menos 32 caracteres aleatórios.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key deve possuir pelo menos 32 bytes.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SimuladorMegaHair";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SimuladorMegaHair.Clients";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer =
                builder.Configuration["Jwt:Issuer"] ?? "SimuladorMegaHair",

            ValidAudience =
                builder.Configuration["Jwt:Audience"] ?? "SimuladorMegaHair.Clients",

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

// ==========================================================
// BUILD
// ==========================================================

var app = builder.Build();

// Diagnóstico inicial
app.Logger.LogInformation(
    "Catálogo carregado do diretório: {CatalogPath}",
    catalogPath);

app.Logger.LogInformation(
    "Manifesto localizado em: {ManifestPath}",
    manifestPath);

using (var scope = app.Services.CreateScope())
{
    var replicateOpts = scope.ServiceProvider
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<ReplicateOptions>>()
        .Value;

    if (string.IsNullOrWhiteSpace(replicateOpts.ApiToken))
    {
        app.Logger.LogWarning(
            "Replicate:ApiToken não configurado. " +
            "As operações que dependem do Replicate não funcionarão.");
    }
}

// ==========================================================
// PIPELINE HTTP
// ==========================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "MegaHair AI v1");

        options.RoutePrefix = "swagger";
    });

    // Compatibilidade com o endereço padrão aberto por alguns perfis/IDE:
    // https://localhost:7064/index.html.
    app.MapGet("/index.html", () => Results.Redirect("/swagger"));
}

app.UseExceptionHandler(erro => erro.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.RequestServices.GetRequiredService<ILogger<Program>>()
        .LogError(ex, "Erro não tratado em {Metodo} {Caminho}", ctx.Request.Method, ctx.Request.Path);
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        erro = "Erro interno ao processar a requisição.",
        detalhe = app.Environment.IsDevelopment() ? ex?.ToString() : null
    });
}));

app.UseHttpsRedirection();
app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapControllers();

// ==========================================================
// MIGRAÇÃO DO BANCO
// ==========================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
}

app.Run();
