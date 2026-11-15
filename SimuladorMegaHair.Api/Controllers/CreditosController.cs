using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Api.Services;

namespace SimuladorMegaHair.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/creditos")]
public sealed class CreditosController(
    CreditosSimulacao creditos, IHttpClientFactory clientes, IConfiguration config, PainelPiloto painel) : ControllerBase
{
    [HttpGet("oferta")]
    public async Task<IActionResult> Oferta(CancellationToken ct)
    {
        var ajustes = await painel.LerAsync(ct);
        return Ok(new
        {
            ativo = creditos.Ativo && ajustes.PublicoAtivo && !string.IsNullOrWhiteSpace(Token),
            preco = ajustes.Preco,
            quantidade = ajustes.Quantidade
        });
    }

    private string? Token => config["MercadoPago:AccessToken"];

    public sealed record DadosPix(
        Guid PedidoId,
        [EmailAddress, Required, MaxLength(254)] string Email,
        [RegularExpression("^[0-9]{11}$"), Required] string Cpf);

    [HttpPost("pix")]
    [EnableRateLimiting("PixEscrita")]
    public async Task<IActionResult> CriarPix(DadosPix dados, CancellationToken ct)
    {
        if (!creditos.Ativo || !(await painel.LerAsync(ct)).PublicoAtivo || string.IsNullOrWhiteSpace(Token))
            return StatusCode(503);
        var carteira = CreditosSimulacao.Carteira(Request.Headers["X-Simulador-Carteira"].ToString());
        if (carteira is null || dados.PedidoId == Guid.Empty) return BadRequest();
        var pedido = await creditos.PedidoAsync(dados.PedidoId, carteira, criar: true, ct);
        if (pedido is null) return NotFound();
        if (pedido.Pagamento.HasValue) return await Consultar(pedido, ct);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(new
            {
                transaction_amount = pedido.Valor,
                payment_method_id = "pix",
                external_reference = pedido.Id.ToString("N"),
                description = $"{config["Creditos:NomeNoPix"] ?? "Simulador Mega Hair"} - {pedido.Creditos} simulações",
                payer = new { email = dados.Email, identification = new { type = "CPF", number = dados.Cpf } }
            })
        };
        req.Headers.Add("X-Idempotency-Key", pedido.Id.ToString());
        using var resposta = await EnviarAsync(req, ct);
        if (resposta is null || !resposta.IsSuccessStatusCode) return StatusCode(502);
        using var json = await resposta.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (json is null || !json.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out var pagamento))
            return StatusCode(502);
        await creditos.RegistrarPagamentoAsync(pedido.Id, pagamento, ct);
        return await Responder(pedido with { Pagamento = pagamento }, json.RootElement, ct);
    }

    [HttpGet("pix/{id:guid}")]
    [EnableRateLimiting("PixConsulta")]
    public async Task<IActionResult> StatusPix(Guid id, CancellationToken ct)
    {
        if (!creditos.Ativo || string.IsNullOrWhiteSpace(Token)) return StatusCode(503);
        var carteira = CreditosSimulacao.Carteira(Request.Headers["X-Simulador-Carteira"].ToString());
        if (carteira is null) return BadRequest();
        var pedido = await creditos.PedidoAsync(id, carteira, criar: false, ct);
        if (pedido?.Pagamento is null) return NotFound();
        return await Consultar(pedido, ct);
    }

    private async Task<IActionResult> Consultar(CreditosSimulacao.PedidoPix pedido, CancellationToken ct)
    {
        if (pedido.Liberado) return Ok(new { status = "approved" });
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{pedido.Pagamento}");
        using var resposta = await EnviarAsync(req, ct);
        if (resposta is null || !resposta.IsSuccessStatusCode) return StatusCode(502);
        using var json = await resposta.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return json is null ? StatusCode(502) : await Responder(pedido, json.RootElement, ct);
    }

    private async Task<IActionResult> Responder(CreditosSimulacao.PedidoPix pedido, JsonElement pagamento, CancellationToken ct)
    {
        if (!pagamento.TryGetProperty("id", out var id) || id.GetInt64() != pedido.Pagamento
            || Texto(pagamento, "external_reference") != pedido.Id.ToString("N")
            || Texto(pagamento, "payment_method_id") != "pix"
            || Texto(pagamento, "currency_id") != "BRL"
            || !pagamento.TryGetProperty("transaction_amount", out var valor) || valor.GetDecimal() != pedido.Valor)
            return StatusCode(502);

        var status = Texto(pagamento, "status");
        if (status == "approved")
            await creditos.LiberarAsync(pedido.Id, id.GetInt64(), ct);

        string? copiaECola = null;
        string? qr = null;
        if (status == "pending"
            && pagamento.TryGetProperty("point_of_interaction", out var interacao)
            && interacao.TryGetProperty("transaction_data", out var transacao))
        {
            copiaECola = Texto(transacao, "qr_code");
            qr = Texto(transacao, "qr_code_base64");
        }
        Response.Headers.CacheControl = "no-store";
        return Ok(new { status, copiaECola, qr });
    }

    private async Task<HttpResponseMessage?> EnviarAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        try { return await clientes.CreateClient("MercadoPago").SendAsync(request, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException) { return null; }
    }

    private static string? Texto(JsonElement obj, string campo) =>
        obj.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;
}
