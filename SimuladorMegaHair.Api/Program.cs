using Microsoft.EntityFrameworkCore;
using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Configuration;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;

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
    builder.Configuration.GetSection(OpenAIOptions.Section));

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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ═══════════════════════════════════════════════════════════
//  BUILD & PIPELINE
// ═══════════════════════════════════════════════════════════

var app = builder.Build();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MegaHair AI v1");
    options.RoutePrefix = string.Empty;
});

// Middlewares
app.UseCors("AllowAll");
app.UseStaticFiles();
app.MapControllers();

// Migração automática do banco
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();