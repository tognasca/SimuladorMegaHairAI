using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimuladorMegaHair.Infrastructure.Configuration;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Cliente OpenAI gpt-image-1 — mesma família de modelo do ChatGPT.
/// Foto + prompt curto. Sem máscara obrigatória.
/// </summary>
public sealed class OpenAiHairClient
{
    private readonly HttpClient _http;
    private readonly OpenAIOptions _opts;
    private readonly ILogger<OpenAiHairClient> _log;

    private const string EditsUrl = "https://api.openai.com/v1/images/edits";

    public OpenAiHairClient(HttpClient http, IOptions<OpenAIOptions> opts, ILogger<OpenAiHairClient> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(_opts.ApiToken)
        && !string.IsNullOrWhiteSpace(_opts.Model);

    public async Task<byte[]> EditarCabeloAsync(string imagemPngPath, string prompt, CancellationToken ct)
    {
        if (!Configurado)
            throw new InvalidOperationException(
                "OpenAI não configurada. Defina OpenAI:ApiToken (OpenAI__ApiToken) e Model=gpt-image-1.5.");

        // Preferir retrato quando a foto original é mais alta que larga (evita cortar o corpo).
        var size = string.IsNullOrWhiteSpace(_opts.Size) ? "1024x1024" : _opts.Size;
        if (size is "1024x1024" or "auto" or "")
        {
            try
            {
                var info = SixLabors.ImageSharp.Image.Identify(imagemPngPath);
                if (info is not null && info.Height > info.Width * 1.1)
                    size = "1024x1536";
                else if (info is not null && info.Width > info.Height * 1.1)
                    size = "1536x1024";
            }
            catch { /* mantém size configurado */ }
        }

        await using var imageStream = File.OpenRead(imagemPngPath);
        using var form = new MultipartFormDataContent();

        form.Add(new StringContent(_opts.Model), "model");
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent("1"), "n");

        if (_opts.Model.Contains("gpt-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(_opts.Quality))
                form.Add(new StringContent(_opts.Quality), "quality");
            form.Add(new StringContent("high"), "input_fidelity");
        }

        var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", "image.png");

        using var req = new HttpRequestMessage(HttpMethod.Post, EditsUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
        req.Content = form;

        _log.LogInformation("[OPENAI] model={Model} size={Size}", _opts.Model, size);
        _log.LogInformation("[OPENAI] prompt={Prompt}", prompt);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("[OPENAI] {Status} {Body}", resp.StatusCode, body);
            throw new HttpRequestException(
                $"Erro OpenAI: {(int)resp.StatusCode} — {body}", null, resp.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            throw new InvalidOperationException("Resposta OpenAI sem data[].");

        var item = data[0];
        if (item.TryGetProperty("b64_json", out var b64))
            return Convert.FromBase64String(b64.GetString()!);

        if (item.TryGetProperty("url", out var urlEl))
            return await _http.GetByteArrayAsync(urlEl.GetString()!, ct);

        throw new InvalidOperationException("Resposta OpenAI sem b64_json nem url.");
    }

    public static string BuildPromptChatGptStyle(
        string comprimento, string cor, string tipoCabelo, string modo = "Extend")
    {
        var corEn = MapCor(cor);
        var texEn = MapTextura(tipoCabelo);
        var (lenEn, whereEn) = MapComprimento(comprimento);

        if (string.Equals(modo, "Recolor", StringComparison.OrdinalIgnoreCase))
        {
            return
                "Keep the exact same person, face, body, clothing, pose, lighting and background. " +
                $"Only change the hair color to {corEn}, same length and style. " +
                "Photorealistic photograph, no accessories, no headband, no ponytail.";
        }

        if (string.Equals(modo, "Shorten", StringComparison.OrdinalIgnoreCase))
        {
            return
                "Keep the exact same person, face, body, clothing, pose, lighting and background. " +
                $"Only change the hair to {corEn} {texEn} hair, {lenEn}, ending {whereEn}, worn loose and down. " +
                "No ponytail, no bun, no braid, no headband, no accessories. Photorealistic photograph.";
        }

        return
            "Keep the exact same woman, the exact same face, body, clothing, pose, lighting and background. " +
            $"Only change the hair: apply realistic long {corEn} hair extensions with {texEn} texture, {lenEn}, " +
            $"falling naturally loose over both shoulders and the chest, ending {whereEn}. " +
            "Hair must be completely loose and down — no ponytail, no pigtails, no bun, no braid, no headband, " +
            "no hair tie, no clips, no accessories. Seamless blend at the roots, natural gravity, photorealistic. " +
            "This is a professional virtual hair-extension preview, not a new portrait.";
    }

    private static string MapCor(string? cor)
    {
        var c = (cor ?? "").ToLowerInvariant()
            .Replace("á", "a").Replace("ã", "a").Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i").Replace("ó", "o").Replace("ô", "o").Replace("ú", "u");
        if (c.Contains("platin") || c.Contains("branco")) return "icy platinum blonde";
        if (c.Contains("loiro") && c.Contains("escuro")) return "dark blonde with soft ash-brown undertones";
        if (c.Contains("loiro") && c.Contains("claro")) return "light golden blonde";
        if (c.Contains("loiro") || c.Contains("blond")) return "golden blonde";
        if (c.Contains("ruivo") || c.Contains("cobre")) return "warm copper red";
        if (c.Contains("castanho") && c.Contains("claro")) return "light brown";
        if (c.Contains("castanho") && c.Contains("escuro")) return "dark brown";
        if (c.Contains("castanho") || c.Contains("brown")) return "medium brown";
        if (c.Contains("preto") || c.Contains("black")) return "natural black";
        if (c.Contains("chocolate")) return "rich chocolate brown";
        if (c.Contains("mel")) return "honey blonde";
        if (c.Contains("grisalh") || c.Contains("prata")) return "silver gray";
        return string.IsNullOrWhiteSpace(cor) ? "natural matching the roots" : cor.Trim();
    }

    private static string MapTextura(string? tipo)
    {
        var t = (tipo ?? "").ToLowerInvariant();
        if (t.Contains("cresp") || t.Contains("afro")) return "tight coily";
        if (t.Contains("cach") || t.Contains("curl")) return "defined curly";
        if (t.Contains("ondul") || t.Contains("wav")) return "soft wavy";
        return "straight sleek";
    }

    private static (string len, string where) MapComprimento(string? c)
    {
        var m = System.Text.RegularExpressions.Regex.Match(c ?? "", @"\d+");
        int cm = m.Success && int.TryParse(m.Value, out var v) ? v : 60;
        return cm switch
        {
            <= 30 => ("shoulder-length", "at the shoulders"),
            <= 42 => ("almost shoulder-length", "just above the chest"),
            <= 50 => ("chest-length", "at the chest"),
            <= 60 => ("long", "just below the chest"),
            <= 75 => ("very long", "near the navel"),
            <= 90 => ("extra-long waist-length", "at the waist"),
            _ => ("extra-long", "below the waist")
        };
    }
}
