// Catálogo de máscaras: carrega e posiciona a silhueta no rosto. Usado pelo HairZoneGenerator.
// NuGet: apenas SixLabors.ImageSharp (já usado no projeto).
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Services;

public record CatalogManifest(
    int[] canvas, float head_center_x, float head_top_y,
    float head_height_px, float head_width_px, float px_per_cm);

/// Carrega e posiciona a máscara do catálogo no rosto.
public sealed class HairMaskBuilder
{
    private readonly string _catalogDir;
    private readonly CatalogManifest _m;

    public HairMaskBuilder(string catalogDir)
    {
        _catalogDir = catalogDir;
        _m = JsonSerializer.Deserialize<CatalogManifest>(
            File.ReadAllText(Path.Combine(catalogDir, "manifest.json")))!;
    }

    public static int NearestLength(int cm)
    {
        int[] opts = { 10, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90 };
        return opts.OrderBy(o => Math.Abs(o - cm)).First();
    }

    public static string NormalizeTexture(string? tipo)
    {
        var t = (tipo ?? "").ToLowerInvariant();
        if (t.Contains("crespo") || t.Contains("coily") || t.Contains("afro")) return "crespo";
        if (t.Contains("cach") || t.Contains("curly")) return "cacheado";
        if (t.Contains("ondul") || t.Contains("wavy")) return "ondulado";
        return "liso";
    }

    public static string NormalizeStyle(string? e)
    {
        var t = (e ?? "").ToLowerInvariant();
        if (t.Contains("colad") || t.Contains("sleek")) return "colado";
        if (t.Contains("volum")) return "volumoso";
        if (t.Contains("later") || t.Contains("side")) return "lateral";
        return "classico";
    }

    public Image<L8> Build(int photoW, int photoH, HeadBox head, int cm, string? tipo, string? estilo, out double areaRatio)
    {
        var file = Path.Combine(_catalogDir,
            $"mask_{NearestLength(cm)}cm_{NormalizeTexture(tipo)}_{NormalizeStyle(estilo)}.png");
        using var src = Image.Load<L8>(file);

        float scale = head.Height / _m.head_height_px;
        float scaleX = Math.Clamp(head.Width / _m.head_width_px, scale * 0.8f, scale * 1.25f);
        int newW = Math.Max(1, (int)(src.Width * scaleX));
        int newH = Math.Max(1, (int)(src.Height * scale));
        src.Mutate(x => x.Resize(newW, newH));

        int offX = (int)(head.CenterX - _m.head_center_x * scaleX);
        int offY = (int)(head.TopY - _m.head_top_y * scale);

        var canvas = new Image<L8>(photoW, photoH, new L8(0));
        canvas.Mutate(c => c.DrawImage(src, new Point(offX, offY), 1f));

        long white = 0;
        for (int y = 0; y < photoH; y++)
            for (int x = 0; x < photoW; x++)
                if (canvas[x, y].PackedValue > 127) white++;
        areaRatio = white / (double)(head.Width * head.Height);
        return canvas;
    }
}