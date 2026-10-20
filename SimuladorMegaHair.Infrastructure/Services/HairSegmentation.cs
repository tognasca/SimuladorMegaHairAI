using System.Linq;
using Microsoft.Extensions.Logging;
// Segmentação de cabelo (Grounded-SAM no Replicate).
// Sem Drawing: só ImageSharp principal.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class HairSegmenter
{
    private readonly HttpClient _http;
    private readonly string _versionHash;
    private readonly ILogger<HairSegmenter> _log;

    // Versão verificada: schananas/grounded_sam
    // 171d73909aa0cd42bc873d5b82d795eef90ecb24001da095af8708d37548b02a
    // (entradas: image, mask_prompt, negative_mask_prompt; saída: lista de URLs de imagem)
    public HairSegmenter(HttpClient http, string apiToken, string versionHash,
        ILogger<HairSegmenter> log)
    {
        _http = http; _versionHash = versionHash; _log = log;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        _http.Timeout = TimeSpan.FromMinutes(2);
    }

    /// Devolve a máscara do cabelo (branco = cabelo) redimensionada para w x h.
    public async Task<Image<L8>> SegmentAsync(byte[] imagePng, int w, int h, CancellationToken ct = default, string? debugFolder = null)
    {
        var body = new
        {
            version = _versionHash,
            input = new
            {
                image = "data:image/png;base64," + Convert.ToBase64String(imagePng),
                mask_prompt = "hair",
                negative_mask_prompt = "car"   // termo ausente em retratos: não subtrai nada
            }
        };
        var payload = JsonSerializer.Serialize(body);
        string json;
        for (int attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
            req.Headers.Add("Prefer", "wait");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req, ct);
            json = await res.Content.ReadAsStringAsync(ct);

            if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 6)
            {
                var wait = TimeSpan.FromSeconds(Math.Min(20, 4 * attempt));
                _log.LogWarning("[SEG] 429 do Replicate; nova tentativa em {S}s ({A}/5)", wait.TotalSeconds, attempt);
                await Task.Delay(wait, ct);
                continue;
            }
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Segmentação {(int)res.StatusCode}: {json}", null, res.StatusCode);
            break;
        }

        var root = JsonDocument.Parse(json).RootElement.Clone();
        var status = root.GetProperty("status").GetString();
        while (status is "starting" or "processing")
        {
            await Task.Delay(1500, ct);
            var poll = await _http.GetStringAsync(root.GetProperty("urls").GetProperty("get").GetString()!, ct);
            root = JsonDocument.Parse(poll).RootElement.Clone();
            status = root.GetProperty("status").GetString();
        }
        if (status != "succeeded")
            throw new InvalidOperationException("Segmentação falhou: " + root.GetProperty("error"));

        // Saída (confirmada pelo log): annotated_picture_mask, neg_annotated_picture_mask, mask, inverted_mask.
        // Escolhemos pelo NOME do arquivo, não por heurística.
        var outs = root.GetProperty("output");
        var urls = outs.EnumerateArray().Select(e => e.GetString()!).ToList();
        string Nome(string u) => Path.GetFileName(new Uri(u).AbsolutePath);
        _log.LogInformation("[SEG] o modelo devolveu {N} saída(s): {Nomes}",
            urls.Count, string.Join(", ", urls.Select(Nome)));

        var maskUrl = urls.FirstOrDefault(u => string.Equals(Nome(u), "mask.jpg", StringComparison.OrdinalIgnoreCase))
                      ?? urls.FirstOrDefault(u => Nome(u).StartsWith("mask.", StringComparison.OrdinalIgnoreCase));
        if (maskUrl is null)
            throw new InvalidOperationException("O modelo de segmentação não devolveu a saída 'mask'.");

        var dbgId = Guid.NewGuid().ToString("N");
        if (debugFolder is not null)
        {
            // também guarda a imagem anotada, para ver o que o modelo detectou
            var anot = urls.FirstOrDefault(u => Nome(u).StartsWith("annotated_picture_mask", StringComparison.OrdinalIgnoreCase));
            if (anot is not null)
                await File.WriteAllBytesAsync(Path.Combine(debugFolder, $"dbg_seg_{dbgId}_annotated.jpg"),
                    await _http.GetByteArrayAsync(anot, ct), ct);
        }

        var bytes = await _http.GetByteArrayAsync(maskUrl, ct);
        if (debugFolder is not null)
            await File.WriteAllBytesAsync(Path.Combine(debugFolder, $"dbg_seg_{dbgId}_mask.jpg"), bytes, ct);

        var img = Image.Load<L8>(bytes);
        long white = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y].PackedValue > 127) white++;
        double frac = white / (double)((long)img.Width * img.Height);
        _log.LogInformation("[SEG] mask: branco={F:P2}", frac);

        if (frac < 0.001)
        {
            img.Dispose();
            throw new InvalidOperationException("O modelo não detectou cabelo nesta foto.");
        }

        if (img.Width != w || img.Height != h) img.Mutate(x => x.Resize(w, h));
        return img;
    }
}