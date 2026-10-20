using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

public record FaceBox(float X, float Y, float Width, float Height, float Confidence);

/// <summary>
/// Detecta rostos usando UltraFace (modelo ONNX inspirado no MediaPipe).
/// Retorna coordenadas do rosto para gerar máscara precisa.
/// </summary>
public class FaceDetector : IDisposable
{
    private readonly InferenceSession _session;
    private const int InputWidth = 320;
    private const int InputHeight = 240;

    public FaceDetector(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Modelo ONNX não encontrado.", modelPath);

        _session = new InferenceSession(modelPath);
    }

    public FaceBox? DetectarRosto(string imagePath)
    {
        using var image = Image.Load<Rgb24>(imagePath);
        int originalWidth = image.Width;
        int originalHeight = image.Height;

        // Redimensiona para o tamanho de entrada do modelo
        using var resized = image.Clone(ctx => ctx.Resize(InputWidth, InputHeight));

        // Prepara o tensor de entrada
        var input = new DenseTensor<float>(new[] { 1, 3, InputHeight, InputWidth });

        resized.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Normalização: (pixel - 127) / 128
                    input[0, 0, y, x] = (row[x].R - 127f) / 128f;
                    input[0, 1, y, x] = (row[x].G - 127f) / 128f;
                    input[0, 2, y, x] = (row[x].B - 127f) / 128f;
                }
            }
        });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", input)
        };

        using var results = _session.Run(inputs);

        var scores = results.First(x => x.Name == "scores").AsTensor<float>();
        var boxes = results.First(x => x.Name == "boxes").AsTensor<float>();

        // Procura o rosto com maior confiança (índice 1 = face, 0 = background)
        float melhorConfianca = 0;
        int melhorIndice = -1;
        int totalBoxes = scores.Dimensions[1];

        for (int i = 0; i < totalBoxes; i++)
        {
            float confianca = scores[0, i, 1];
            if (confianca > melhorConfianca && confianca > 0.7f)
            {
                melhorConfianca = confianca;
                melhorIndice = i;
            }
        }

        if (melhorIndice == -1)
            return null;

        // Coordenadas normalizadas (0 a 1) → convertidas para o tamanho original
        float x1 = boxes[0, melhorIndice, 0] * originalWidth;
        float y1 = boxes[0, melhorIndice, 1] * originalHeight;
        float x2 = boxes[0, melhorIndice, 2] * originalWidth;
        float y2 = boxes[0, melhorIndice, 3] * originalHeight;

        return new FaceBox(
            X: x1,
            Y: y1,
            Width: x2 - x1,
            Height: y2 - y1,
            Confidence: melhorConfianca
        );
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}