using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Rosto detectado em um quadro da câmera, em frações do quadro (0..1).
public sealed record AvaliacaoRosto(
    bool Encontrado, float X, float Y, float W, float H, float Confianca, int Largura, int Altura);

/// Detecta o rosto em quadros enviados pela tela de captura, com o MESMO detector do pipeline (UltraFace).
/// Mantém um único detector em memória (singleton) e serializa as chamadas.
public sealed class FotoAvaliadorService : IDisposable
{
    private readonly string _modelo;
    private readonly string _tmp;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FaceDetector? _det;

    public FotoAvaliadorService(IWebHostEnvironment env)
    {
        _modelo = LocalizarModelo(env);
        _tmp = Path.Combine(Path.GetTempPath(), "megahair-frames");
        Directory.CreateDirectory(_tmp);
    }

    public async Task<AvaliacaoRosto> AvaliarAsync(Stream imagem, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await imagem.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var info = Image.Identify(bytes);

        var path = Path.Combine(_tmp, $"{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, bytes, ct);

        await _lock.WaitAsync(ct);
        try
        {
            _det ??= new FaceDetector(_modelo);
            var r = _det.DetectarRosto(path);
            if (r is null)
                return new AvaliacaoRosto(false, 0, 0, 0, 0, 0, info.Width, info.Height);

            float x = (float)r.X, y = (float)r.Y, w = (float)r.Width, h = (float)r.Height;
            if (w > 1.5f || h > 1.5f)   // veio em pixels → normaliza
            {
                x /= info.Width; w /= info.Width;
                y /= info.Height; h /= info.Height;
            }
            return new AvaliacaoRosto(true, x, y, w, h, (float)r.Confidence, info.Width, info.Height);
        }
        finally
        {
            _lock.Release();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    private static string LocalizarModelo(IWebHostEnvironment env)
    {
        const string nome = "ultraface.onnx";
        foreach (var c in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Models", nome),
            Path.Combine(env.ContentRootPath, "Models", nome),
        })
            if (File.Exists(c)) return c;
        throw new FileNotFoundException($"'{nome}' não encontrado.");
    }

    public void Dispose()
    {
        _det?.Dispose();
        _lock.Dispose();
    }
}
