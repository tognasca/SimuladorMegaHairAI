using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SimuladorMegaHair.Api.Services;

public sealed class PixReconciliacao(
    IServiceScopeFactory scopes, IHttpClientFactory clientes, IConfiguration config,
    ILogger<PixReconciliacao> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var relogio = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await relogio.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var creditos = scope.ServiceProvider.GetRequiredService<CreditosSimulacao>();
                await creditos.ReconciliarExpiradasAsync(stoppingToken);
                var token = config["MercadoPago:AccessToken"];
                if (string.IsNullOrWhiteSpace(token)) continue;

                var pedidos = await creditos.PedidosParaReconciliarAsync(stoppingToken);
                using var cliente = clientes.CreateClient("MercadoPago");
                foreach (var pedido in pedidos)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{pedido.Pagamento}");
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        using var resp = await cliente.SendAsync(req, stoppingToken);
                        if (!resp.IsSuccessStatusCode) continue;
                        using var json = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: stoppingToken);
                        if (json is null || !Confere(pedido, json.RootElement)) continue;
                        var status = Texto(json.RootElement, "status");
                        if (status == "approved")
                            await creditos.LiberarAsync(pedido.Id, pedido.Pagamento!.Value, stoppingToken);
                        else if (status is "cancelled" or "rejected" or "refunded" or "charged_back")
                            await creditos.EncerrarReconciliacaoAsync(pedido.Id, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested &&
                        ex is HttpRequestException or OperationCanceledException or JsonException)
                    {
                        logger.LogWarning("Consulta periódica ao Mercado Pago falhou para um pedido.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao reconciliar lote de pagamentos Pix.");
            }
        }
    }

    internal static bool Confere(CreditosSimulacao.PedidoPix pedido, JsonElement pagamento) =>
        pagamento.ValueKind == JsonValueKind.Object
        && pagamento.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt64(out var idRecebido) && idRecebido == pedido.Pagamento
        && Texto(pagamento, "external_reference") == pedido.Id.ToString("N")
        && Texto(pagamento, "payment_method_id") == "pix"
        && Texto(pagamento, "currency_id") == "BRL"
        && pagamento.TryGetProperty("transaction_amount", out var valor) && valor.ValueKind == JsonValueKind.Number
        && valor.TryGetDecimal(out var valorRecebido) && valorRecebido == pedido.Valor;

    private static string? Texto(JsonElement json, string campo) =>
        json.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;
}
