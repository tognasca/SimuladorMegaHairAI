using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using System.Text;

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
});

// ═══════════════════════════════════════════════════════════
//  CONFIGURAÇÕES (IOptions)
// ═══════════════════════════════════════════════════════════

builder.Services.Configure<SimulacaoOptions>(
    builder.Configuration.GetSection(SimulacaoOptions.Section));

builder.Services.Configure<ReplicateOptions>(
    builder.Configuration.GetSection(ReplicateOptions.Section));

builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection(OpenAIOptions.Section));// ═══════════════════════════════════════════════════════════
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
//  CORS — restrito às origens conhecidas do salão (Web/App),
//  configuradas em "AllowedOrigins" no appsettings. Nunca use
//  AllowAnyOrigin() aqui: a API expõe dados pessoais de clientes.
// ═══════════════════════════════════════════════════════════

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ═══════════════════════════════════════════════════════════
//  AUTENTICAÇÃO (JWT)
// ═══════════════════════════════════════════════════════════

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key não configurado. Defina a variável de ambiente Jwt__Key " +
        "(mín. 32 caracteres aleatórios) — nunca deixe isso em appsettings.json versionado.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "SimuladorMegaHair",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "SimuladorMegaHair.Clients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

//builder.Services.AddApplicationInsightsTelemetry();

// ═══════════════════════════════════════════════════════════
//  BUILD & PIPELINE
// ═══════════════════════════════════════════════════════════

var app = builder.Build();

// Aviso cedo (não derruba a API) se o token do Replicate não estiver
// configurado — evita descobrir isso só quando uma cliente já está com a
// foto na mão esperando o resultado. Ver: Replicate:ApiToken deve vir de
// variável de ambiente/user-secrets, nunca de appsettings.json versionado.
{
    using var scope = app.Services.CreateScope();
    var replicateOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<ReplicateOptions>>().Value;
    if (string.IsNullOrWhiteSpace(replicateOpts.ApiToken))
    {
        app.Logger.LogWarning(
            "⚠️  Replicate:ApiToken não está configurado. Toda chamada de " +
            "simulação vai falhar com 502 até isso ser corrigido " +
            "(dotnet user-secrets set Replicate:ApiToken \"...\" " +
            "ou variável de ambiente Replicate__ApiToken).");
    }
}

// Swagger — só em desenvolvimento. Em produção, a documentação interativa
// e o "Try it out" da API não devem ficar publicamente acessíveis.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MegaHair AI v1");
        options.RoutePrefix = "swagger";
    });
}

// Middlewares
app.UseHttpsRedirection();
app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();

// Migração automática do banco
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();