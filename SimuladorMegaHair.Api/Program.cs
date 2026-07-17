using SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Infrastructure.Data;
using SimuladorMegaHair.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger
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

// Banco de dados
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Serviços
builder.Services.AddScoped<IOrcamentoService, OrcamentoService>();
builder.Services.AddHttpClient<IImageSimulationService, OpenAiImageSimulationService>();

// CORS (para o MAUI acessar a API localmente)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

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

// Criar banco automaticamente na primeira execução
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();