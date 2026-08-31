using Microsoft.Extensions.Logging;
using SimuladorMegaHair.Infrastructure.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Segmentação de cabelo por MODELO DE IA REAL (Grounded SAM: Grounding
/// DINO + Segment Anything), via Replicate — em vez da máscara puramente
/// geométrica/heurística que o projeto usava antes.
///
/// POR QUE ESSE MODELO ESPECIFICAMENTE:
/// - Já é usado em produção por outro produto (virtual try-on, doiwear.it).
/// - Está listado na coleção OFICIAL de detecção/segmentação do Replicate
///   (replicate.com/collections/ai-detect-objects), recomendado
///   explicitamente para "criar máscaras para inpainting via prompt de texto".
/// - Aceita um prompt positivo ("hair") e um prompt NEGATIVO
///   ("face, skin, eyebrows...") — ou seja, o próprio modelo já é treinado
///   para excluir rosto/pele da máscara, não é uma suposição nossa.
/// - Roda em GPU, ~3 segundos por predição — compatível com o tempo de
///   simulação já existente (que já espera pelo FLUX Fill).
///
/// IMPORTANTE (honestidade sobre o que É verificado vs. o que precisa
/// validação final): a existência do modelo, seu uso em produção e o
/// conceito de entrada (imagem + prompt positivo + prompt negativo) foram
/// confirmados na documentação pública do Replicate e no repositório do
/// autor. Os NOMES EXATOS dos campos JSON abaixo (mask_prompt,
/// negative_mask_prompt, adjustment_factor) seguem a convenção documentada
/// publicamente pelo autor do modelo; ainda assim, antes de operar em
/// produção, teste uma chamada real pelo Playground
/// (https://replicate.com/schananas/grounded_sam) e confirme os nomes de
/// campo batem com a versão atual — modelos de terceiros no Replicate podem
/// mudar o schema entre versões. Por isso todo o método é cercado de
/// try/catch e NUNCA quebra o fluxo: se algo não bater, cai para a máscara
/// geométrica (ver HairMaskGenerator), que continua funcionando sozinha.
/// </summary>
public static class HairSegmentationService
{
    private const string BaseUrl = "https://api.replicate.com/v1";

    public static async Task<string?> SegmentarCabeloAsync(
        HttpClient http,
        ReplicateOptions opts,
        string imagemPath,
        string outputFolder,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation("[IA-MASK] Chamando {Owner}/{Name} (Grounded SAM) para segmentar cabelo...",
                opts.HairSegmentOwner, opts.HairSegmentName);

            var imagemB64 = ConverterBase64(imagemPath);

            var body = new
            {
                input = new
                {
                    image = imagemB64,
                    mask_prompt = "hair",
                    negative_mask_prompt = "face, skin, eyebrows, eyelashes, eyes, mouth, lips, ears, neck skin, background, clothes",
                    adjustment_factor = 0
                }
            };

            var url = $"{BaseUrl}/models/{opts.HairSegmentOwner}/{opts.HairSegmentName}/predictions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiToken);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[IA-MASK] Falha ao iniciar predição ({Status}): {Err}", resp.StatusCode, err);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var predId = doc.RootElement.GetProperty("id").GetString()!;

            var maskUrl = await PollAsync(http, opts, predId, logger, ct);
            if (maskUrl is null) return null;

            var bytes = await http.GetByteArrayAsync(maskUrl, ct);
            Directory.CreateDirectory(outputFolder);
            var path = Path.Combine(outputFolder, $"ia_mask_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, bytes, ct);

            logger.LogInformation("[IA-MASK] Máscara de IA obtida com sucesso: {Path}", path);
            return path;
        }
        catch (Exception ex)
        {
            // Nunca deixa a IA de máscara derrubar a simulação — cai para
            // o fallback geométrico (defesa em profundidade).
            logger.LogWarning(ex, "[IA-MASK] Segmentação por IA indisponível; usando fallback geométrico.");
            return null;
        }
    }

    private static async Task<string?> PollAsync(
        HttpClient http, ReplicateOptions opts, string predId, ILogger logger, CancellationToken ct)
    {
        for (int i = 0; i < opts.HairSegmentMaxPollAttempts; i++)
        {
            await Task.Delay(opts.HairSegmentPollIntervalMs, ct);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/predictions/{predId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiToken);
            using var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status == "succeeded")
            {
                var output = doc.RootElement.GetProperty("output");

                // O modelo retorna um array de máscaras (uma por prompt
                // combinado); como enviamos só "hair", usamos a primeira.
                return output.ValueKind switch
                {
                    JsonValueKind.Array when output.GetArrayLength() > 0 => output[0].GetString(),
                    JsonValueKind.String => output.GetString(),
                    _ => null
                };
            }

            if (status is "failed" or "canceled")
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "sem detalhe";
                logger.LogWarning("[IA-MASK] Predição {Id} {Status}: {Err}", predId, status, err);
                return null;
            }
        }

        logger.LogWarning("[IA-MASK] Timeout aguardando segmentação (predição {Id}).", predId);
        return null;
    }

    private static string ConverterBase64(string caminho)
    {
        var bytes = File.ReadAllBytes(caminho);
        var ext = Path.GetExtension(caminho).TrimStart('.').ToLowerInvariant();
        var mime = ext is "jpg" or "jpeg" ? "image/jpeg" : ext is "webp" ? "image/webp" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
