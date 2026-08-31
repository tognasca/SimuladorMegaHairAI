using SimuladorMegaHair.Web.Components;
using SimuladorMegaHair.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Razor Components (Blazor Server) ────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Cliente HTTP para o backend (SimuladorMegaHair.Api) ─────────
// O endereço vem de appsettings.json (seção "Api:BaseUrl") — configure
// para o IP da máquina que roda a API na rede do salão, ex:
// "http://192.168.1.100:5185/". Igual ao app MAUI, TODOS os
// dispositivos (TV, tablets, iPad, celulares) devem apontar para o
// MESMO endereço, para compartilhar clientes/catálogo/histórico.
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5185/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(10); // simulações de IA demoram
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// ⚠️ HTTPS não é opcional aqui: a captura de foto pelo navegador
// (getUserMedia, usada no iPad/Safari e em qualquer navegador moderno)
// só funciona em "contexto seguro" — HTTPS ou localhost. Sem isso, o
// botão de câmera simplesmente não vai aparecer/funcionar no iPad.
// Ver Properties/launchSettings.json e README de deploy para gerar/
// confiar o certificado local em cada dispositivo do salão.
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
