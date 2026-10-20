// Zona onde a edição é PERMITIDA no composite final (não é máscara de geração).
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Services;

public sealed class HairZoneGenerator
{
    private readonly HairMaskBuilder _catalog;
    private readonly ILogger<HairZoneGenerator> _log;

    // Altura da cabeça (topo do cabelo → queixo) = altura da caixa do rosto × fator.
    private const float HeadHeightFactor = 1.45f;

    public HairZoneGenerator(HairMaskBuilder catalog, ILogger<HairZoneGenerator> log)
        => (_catalog, _log) = (catalog, log);

    public async Task<string> GerarAsync(
        string imagemPrepPath, string masksFolder,
        float faceX, float faceY, float faceW, float faceH,
        string comprimento, HairEditMode modo, CancellationToken ct,
        bool ajustada = false, string? tipoCabelo = null, int? cmExato = null)
    {
        var bytes = await File.ReadAllBytesAsync(imagemPrepPath, ct);
        var info = Image.Identify(bytes);
        int w = info.Width, h = info.Height;

        if (faceW <= 1.5f && faceH <= 1.5f)   // caixa normalizada 0..1 → pixels
        {
            faceX *= w; faceW *= w; faceY *= h; faceH *= h;
        }

        float chin = faceY + faceH;
        float headH = faceH * HeadHeightFactor;
        var head = new HeadBox(faceX + faceW / 2f, chin - headH, chin, faceW * 1.1f, headH,
            new Rectangle((int)faceX, (int)faceY, (int)faceW, (int)faceH));

        var m = Regex.Match(comprimento ?? "", @"\d+");
        int cm = m.Success && int.TryParse(m.Value, out var v) ? v : 60;
        // Zona generosa: um pouco mais longa que o pedido; em Recolor/Shorten, comprimento máximo.
        int cmZona = cmExato ?? (modo == HairEditMode.Extend ? (ajustada ? Math.Min(90, cm + 10) : Math.Min(90, cm + 15)) : 90);

        // Catálogo no formato mais largo (crespo + volumoso) só para dar espaço ao cabelo.
        // Zona "ajustada" (fallback sem segmentação): formato do próprio pedido, sem folga larga,
        // para não colar partes do corpo que o modelo tenha alterado.
        using var zona = ajustada
            ? _catalog.Build(w, h, head, cmZona, tipoCabelo, "classico", out _)
            : _catalog.Build(w, h, head, cmZona, "crespo", "volumoso", out _);

        // Calota generosa sobre o topo da cabeça (cabelo existente + volume natural).
        float cx = head.CenterX, cy = head.TopY + head.Height * 0.40f;
        float rx = head.Width * 0.75f, ry = head.Height * 0.75f;
        for (int y = Math.Max(0, (int)(cy - ry)); y <= Math.Min(h - 1, (int)(cy + ry)); y++)
            for (int x = Math.Max(0, (int)(cx - rx)); x <= Math.Min(w - 1, (int)(cx + rx)); x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy <= 1f) zona[x, y] = new L8(255);
            }

        // Alarga a zona (dilatação) para tolerar erro de posição.
        float sigma = Math.Max(2f, head.Width * (ajustada ? 0.06f : 0.12f));
        zona.Mutate(c => c.GaussianBlur(sigma));
        zona.Mutate(c => c.BinaryThreshold(0.08f));

        ProtegerRosto(zona, head);
        zona.Mutate(c => c.GaussianBlur(3f));

        long white = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) if (zona[x, y].PackedValue > 127) white++;
        double frac = white / (double)(w * h);
        _log.LogInformation("[ZONA] imagem {W}x{H} rosto=({X:F0},{Y:F0},{FW:F0},{FH:F0}) cmZona={C} area={A:P1} modo={M}",
            w, h, faceX, faceY, faceW, faceH, cmZona, frac, modo);

        if (frac < 0.005)
            throw new InvalidOperationException(
                "Não consegui delimitar a área do cabelo nesta foto. Envie uma foto frontal, com a cabeça inteira visível.");

        var path = Path.Combine(masksFolder, $"zona_{Guid.NewGuid():N}.png");
        await zona.SaveAsPngAsync(path, ct);
        return path;
    }

    // Proteção do que NUNCA pode mudar: rosto inteiro (retângulo arredondado, cobre o maxilar) e pescoço.
    // Ajuste estas constantes se aparecer cabelo antigo na testa ou corte nas laterais do rosto/pescoço.
    private const float FaceHalfW = 0.53f;  // meia-largura (× largura da caixa do rosto)
    private const float FaceTopOff = -0.03f; // topo (× altura da caixa, a partir do topo da caixa)
    private const float FaceBottom = 1.08f;  // base (× altura da caixa)
    private const float FaceRadius = 0.30f;  // raio dos cantos (× largura da caixa)
    private const float NeckHalfWidth = 0.31f;
    private const float NeckStartY = 0.85f;
    private const float NeckEndY = 1.90f;

    private static void ProtegerRosto(Image<L8> m, HeadBox head)
    {
        var fr = head.FaceRect;

        // 1) Rosto: retângulo arredondado
        float cx = fr.X + fr.Width / 2f;
        float y0 = fr.Y + fr.Height * FaceTopOff, y1 = fr.Y + fr.Height * FaceBottom;
        float cy = (y0 + y1) / 2f;
        float hw = fr.Width * FaceHalfW, hh = (y1 - y0) / 2f;
        float r = Math.Min(fr.Width * FaceRadius, Math.Min(hw, hh));
        for (int y = Math.Max(0, (int)y0); y <= Math.Min(m.Height - 1, (int)y1); y++)
            for (int x = Math.Max(0, (int)(cx - hw)); x <= Math.Min(m.Width - 1, (int)(cx + hw)); x++)
            {
                float dx = Math.Max(Math.Abs(x - cx) - (hw - r), 0f);
                float dy = Math.Max(Math.Abs(y - cy) - (hh - r), 0f);
                if (dx * dx + dy * dy <= r * r) m[x, y] = new L8(0);
            }

        // 2) Pescoço: faixa estreita; o cabelo que cai ao lado dele continua permitido.
        int nx0 = Math.Max(0, (int)(cx - fr.Width * NeckHalfWidth));
        int nx1 = Math.Min(m.Width - 1, (int)(cx + fr.Width * NeckHalfWidth));
        int ny0 = Math.Max(0, (int)(fr.Y + fr.Height * NeckStartY));
        int ny1 = Math.Min(m.Height - 1, (int)(fr.Y + fr.Height * NeckEndY));
        for (int y = ny0; y <= ny1; y++)
            for (int x = nx0; x <= nx1; x++) m[x, y] = new L8(0);
    }
}