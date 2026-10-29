using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// CAMINHOS DOS ASSETS
// ==========================================================

var assetsPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "Assets");

var catalogPath = Path.Combine(
    assetsPath,
    "catalogo");

var manifestPath = Path.Combine(
    catalogPath,
    "manifest.json");

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
// CONTROLLERS
// ==========================================================

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();

// ==========================================================
// SWAGGER + JWT
// ==========================================================

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "MegaHair AI API",
        Version = "v1",
        Description =
            "API para simulação de mega hair com inteligência artificial."
    });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Informe somente o token JWT. " +
                "O Swagger adicionará Bearer automaticamente."
        });
});

// ==========================================================
// CONFIGURAÇÕES
// ==========================================================

builder.Services.Configure<SimulacaoOptions>(
    builder.Configuration.GetSection(
        SimulacaoOptions.Section));

builder.Services.Configure<ReplicateOptions>(
    builder.Configuration.GetSection(
        ReplicateOptions.Section));

builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection(
        OpenAIOptions.Section));

// ==========================================================
// CONFIGURAÇÃO DO PIPELINE
// ==========================================================

var metodo = (
    builder.Configuration["Pipeline:Metodo"]
    ?? "fill-mascara"
)
.Trim()
.ToLowerInvariant();

var segmentacao = builder.Configuration
    .GetValue<int>("Pipeline:Segmentacao", 0);

var metodosPermitidos = new[]
{
    "openai",
    "fill-mascara"
};

if (!metodosPermitidos.Contains(metodo))
{
    throw new InvalidOperationException(
        $"Método de pipeline inválido: {metodo}. " +
        $"Métodos permitidos: {string.Join(", ", metodosPermitidos)}");
}

// ==========================================================
// BANCO DE DADOS
// ==========================================================

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection não configurada.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// ==========================================================
// CATÁLOGO DE CABELOS
// ==========================================================

builder.Services.AddSingleton(
    _ => new HairMaskBuilder(catalogPath));

// ==========================================================
// GERADOR DE ZONA DE CABELO
// ==========================================================

builder.Services.AddScoped<HairZoneGenerator>();

// ==========================================================
// SEGMENTAÇÃO VIA REPLICATE
// ==========================================================

const string DefaultSegmentVersion =
    "171d73909aa0cd42bc873d5b82d795eef90ecb24001da095af8708d37548b02a";

builder.Services
    .AddHttpClient<HairSegmenter>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .AddTypedClient((http, sp) =>
    {
        var apiToken =
            builder.Configuration["Replicate:ApiToken"]
            ?? string.Empty;

        var segmentVersion =
            builder.Configuration["Replicate:SegmentVersion"]
            ?? DefaultSegmentVersion;

        return new HairSegmenter(
            http,
            apiToken,
            segmentVersion,
            sp.GetRequiredService<ILogger<HairSegmenter>>());
    });

// ==========================================================
// SERVIÇOS DE DOMÍNIO
// ==========================================================

builder.Services.AddScoped<
    IOrcamentoService,
    OrcamentoService>();

// ==========================================================
// AVALIAÇÃO DE FOTO
// ==========================================================

builder.Services.AddSingleton<FotoAvaliadorService>();

// ==========================================================
// OPENAI - GERAÇÃO DE MEGA HAIR
// ==========================================================

builder.Services
    .AddHttpClient<OpenAiHairClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(10);
    });

// ==========================================================
// PIPELINE DE SIMULAÇÃO
// ==========================================================

builder.Services
    .AddHttpClient<
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
    .Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// ==========================================================
// AUTENTICAÇÃO JWT
// ==========================================================

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key não configurado.");
}

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key deve possuir pelo menos 32 bytes.");
}

var jwtIssuer =
    builder.Configuration["Jwt:Issuer"]
    ?? "SimuladorMegaHair";

var jwtAudience =
    builder.Configuration["Jwt:Audience"]
    ?? "SimuladorMegaHair.Clients";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),

                ClockSkew = TimeSpan.FromMinutes(1)
            };
    });

builder.Services.AddAuthorization();

// ==========================================================
// BUILD
// ==========================================================

var app = builder.Build();

// ==========================================================
// DIAGNÓSTICO DE INICIALIZAÇÃO
// ==========================================================

app.Logger.LogInformation(
    "MegaHair AI inicializado.");

app.Logger.LogInformation(
    "Ambiente: {Environment}",
    app.Environment.EnvironmentName);

app.Logger.LogInformation(
    "Catálogo: {CatalogPath}",
    catalogPath);

app.Logger.LogInformation(
    "Manifesto: {ManifestPath}",
    manifestPath);

app.Logger.LogInformation(
    "Pipeline selecionado: {Metodo}",
    metodo);

app.Logger.LogInformation(
    "Segmentação configurada: {Segmentacao}",
    segmentacao);

// ==========================================================
// VALIDAÇÃO DAS CONFIGURAÇÕES DE IA
// ==========================================================

using (var scope = app.Services.CreateScope())
{
    var replicateOptions = scope.ServiceProvider
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<ReplicateOptions>>()
        .Value;

    var openAiOptions = scope.ServiceProvider
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<OpenAIOptions>>()
        .Value;

    if (string.IsNullOrWhiteSpace(replicateOptions.ApiToken))
    {
        app.Logger.LogWarning(
            "Replicate:ApiToken não configurado.");
    }

    if (metodo == "openai")
    {
        app.Logger.LogInformation(
            "Pipeline OPENAI selecionado.");

        if (string.IsNullOrWhiteSpace(openAiOptions.ApiToken))
        {
            app.Logger.LogWarning(
                "OpenAI:ApiToken não configurado. " +
                "A geração de imagens não funcionará.");
        }
    }
}

// ==========================================================
// TRATAMENTO GLOBAL DE EXCEÇÕES
// ==========================================================

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features
            .Get<IExceptionHandlerFeature>()
            ?.Error;

        var logger = context.RequestServices
            .GetRequiredService<ILogger<Program>>();

        logger.LogError(
            exception,
            "Erro não tratado em {Method} {Path}",
            context.Request.Method,
            context.Request.Path);

        context.Response.StatusCode = 500;

        await context.Response.WriteAsJsonAsync(new
        {
            erro = "Erro interno ao processar a requisição.",

            detalhe = app.Environment.IsDevelopment()
                ? exception?.ToString()
                : null
        });
    });
});

// ==========================================================
// SWAGGER
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

    app.MapGet(
        "/index.html",
        () => Results.Redirect("/swagger"));
}

// ==========================================================
// PIPELINE HTTP
// ==========================================================

// URLs absolutas devolvidas pela API (imagens, resultados) usam o endereco PUBLICO
// quando PublicBaseUrl esta configurado (necessario atras do Docker/Cloudflare).
var publicBaseUrl = builder.Configuration["PublicBaseUrl"];
if (Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri))
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = publicUri.Scheme;
        context.Request.Host = publicUri.IsDefaultPort
            ? new HostString(publicUri.Host)
            : new HostString(publicUri.Host, publicUri.Port);
        return next();
    });
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseCors("Default");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

// ==========================================================
// MIGRAÇÃO DO BANCO DE DADOS
// ==========================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    try
    {
        app.Logger.LogInformation(
            "Verificando migrations do banco de dados.");

        db.Database.Migrate();

        app.Logger.LogInformation(
            "Banco de dados atualizado com sucesso.");
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(
            ex,
            "Falha ao aplicar migrations. " +
            "Verifique a compatibilidade das migrations " +
            "com o PostgreSQL.");

        throw;
    }
}

// ==========================================================
// INICIALIZAÇÃO
// ==========================================================

app.Logger.LogInformation(
    "MegaHair AI API pronta para receber requisições.");

app.Run();