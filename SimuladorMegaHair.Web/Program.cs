using SimuladorMegaHair.Web.Components;
using SimuladorMegaHair.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Razor Components (Blazor Server) ────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ⚠️ Limite de tamanho de mensagem do SignalR (canal em tempo real do
// Blazor Server). O padrão é pequeno (~32 KB) — insuficiente para uma
// foto capturada pela câmera, que some no JS interop como uma string
// base64 de vários MB. Sem aumentar isso, o circuito é derrubado à
// força assim que a foto tenta trafegar (sintoma: "Connection closed
// with an error" logo após clicar em "Tirar foto").
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024; // 15 MB
});

// ── Cliente HTTP para o backend (SimuladorMegaHair.Api) ─────────
// O endereço vem de appsettings.json (seção "Api:BaseUrl") — configure
// para o IP da máquina que roda a API na rede do salão, ex:
// "http://192.168.1.100:5185/". Igual ao app MAUI, TODOS os
// dispositivos (TV, tablets, iPad, celulares) devem apontar para o
// MESMO endereço, para compartilhar clientes/catálogo/histórico.
//
// FASE 1: a API agora exige o cabeçalho X-Api-Key em todas as chamadas.
// Como este site fala com a API pelo SERVIDOR (Blazor Server), a chave
// fica só aqui — nunca chega ao navegador da cliente.
var megaHairApiKey = builder.Configuration["MEGAHAIR_API_KEY"]
                      ?? Environment.GetEnvironmentVariable("MEGAHAIR_API_KEY")
                      ?? string.Empty;

builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5185/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(10); // simulações de IA demoram

    if (!string.IsNullOrWhiteSpace(megaHairApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", megaHairApiKey);
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(megaHairApiKey))
{
    app.Logger.LogWarning(
        "MEGAHAIR_API_KEY não configurada neste site: todas as chamadas à " +
        "Api serão recusadas com 401. Configure a mesma chave usada na Api " +
        "(variável de ambiente MEGAHAIR_API_KEY).");
}

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