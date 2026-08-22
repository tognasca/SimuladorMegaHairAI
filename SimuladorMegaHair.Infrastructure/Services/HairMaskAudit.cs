using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Grava a máscara e um overlay para conferir se o branco
/// cobriu só o cabelo (e não o rosto).
/// </summary>
public static class HairMaskAudit
{
    public static async Task<string> SalvarAsync(
        string imagemPath,
        string maskPath,
        string auditFolder,
        ILogger logger,
        CancellationToken ct)
    {
        Directory.CreateDirectory(auditFolder);

        var id = Guid.NewGuid().ToString("N")[..12];
        var maskDest = Path.Combine(auditFolder, $"mask_{id}.png");
        var overlayDest = Path.Combine(auditFolder, $"overlay_{id}.png");

        File.Copy(maskPath, maskDest, overwrite: true);

        using var original = await Image.LoadAsync<Rgba32>(imagemPath, ct);
        using var mascara = await Image.LoadAsync<Rgba32>(maskPath, ct);

        if (mascara.Width != original.Width || mascara.Height != original.Height)
            mascara.Mutate(x => x.Resize(original.Width, original.Height));

        var total = original.Width * original.Height;
        var brancos = 0;

        original.ProcessPixelRows(mascara, (imgAcc, mskAcc) =>
        {
            for (int y = 0; y < imgAcc.Height; y++)
            {
                var imgRow = imgAcc.GetRowSpan(y);
                var mskRow = mskAcc.GetRowSpan(y);

                for (int x = 0; x < imgRow.Length; x++)
                {
                    var t = mskRow[x].R / 255f;
                    if (t <= 0.08f)
                        continue;

                    brancos++;
                    ref var p = ref imgRow[x];
                    p.R = (byte)Math.Clamp(p.R * (1 - t * 0.55f) + 255 * t * 0.55f, 0, 255);
                    p.G = (byte)Math.Clamp(p.G * (1 - t * 0.55f), 0, 255);
                    p.B = (byte)Math.Clamp(p.B * (1 - t * 0.55f), 0, 255);
                }
            }
        });

        await original.SaveAsPngAsync(overlayDest, ct);

        var percentual = total == 0 ? 0 : brancos * 100.0 / total;
        logger.LogInformation(
            "Máscara de auditoria {Id}: {P:F1}% da imagem será gerada pela IA. " +
            "Arquivos: {Mask} | {Overlay}",
            id, percentual, maskDest, overlayDest);

        if (percentual > 45)
            logger.LogWarning(
                "Máscara grande ({P:F1}%) — risco de editar fundo, roupa ou parte do rosto. " +
                "Abra o overlay vermelho em masks/audit.",
                percentual);

        return $"masks/audit/overlay_{id}.png";
    }
}
