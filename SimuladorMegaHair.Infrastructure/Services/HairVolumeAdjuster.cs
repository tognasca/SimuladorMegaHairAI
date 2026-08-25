using Microsoft.Extensions.Logging;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class HairVolumeAdjuster
{
    public record VolumeProfile(
        int Nivel,
        int NivelGramas,
        int DeslocamentoX,
        float Opacidade,
        float EscalaX,
        float EscalaY
    );

    // Perfis de volume calibrados para cada nível de Mega Hair
    private static readonly Dictionary<int, VolumeProfile> Perfis = new()
    {
        [1] = new(1, 100, DeslocamentoX: 4, Opacidade: 0.35f, EscalaX: 1.01f, EscalaY: 1.00f),
        [2] = new(2, 200, DeslocamentoX: 8, Opacidade: 0.55f, EscalaX: 1.03f, EscalaY: 1.01f),
        [3] = new(3, 300, DeslocamentoX: 14, Opacidade: 0.75f, EscalaX: 1.05f, EscalaY: 1.02f),
        [4] = new(4, 400, DeslocamentoX: 20, Opacidade: 0.90f, EscalaX: 1.08f, EscalaY: 1.03f)
    };

    public static async Task<string> Aplicar(
        AjustarVolumeRequest req,
        ILogger logger,
        string pastaSaida,
        CancellationToken ct = default)
    {
        // Obtém o perfil correspondente (padrão nível 2 = 200g)
        if (!Perfis.TryGetValue(req.Nivel, out var perfil))
        {
            perfil = Perfis[2];
        }

        logger.LogInformation(
            "[VOLUME] Processando volume Nível {Nivel} ({Gramas}g)...",
            perfil.Nivel, perfil.NivelGramas);

        var caminhoOrigem = req.ImagemResultadoPath ?? req.ImagemOriginalPath;
        if (string.IsNullOrWhiteSpace(caminhoOrigem) || !File.Exists(caminhoOrigem))
        {
            throw new FileNotFoundException("A imagem para ajuste de volume não foi encontrada.", caminhoOrigem);
        }

        var nomeArquivo = $"volume_{perfil.NivelGramas}g_{Guid.NewGuid():N}.png";
        var caminhoDestino = Path.Combine(pastaSaida, nomeArquivo);

        using var imagem = await Image.LoadAsync<Rgba32>(caminhoOrigem, ct);

        // Clona a imagem para criar as camadas de densidade lateral
        using (var camadaEsquerda = imagem.Clone())
        using (var camadaDireita = imagem.Clone())
        {
            // Ajusta escala levemente para dar sensação de corpo e densidade
            int novaLargura = (int)(imagem.Width * perfil.EscalaX);
            int novaAltura = (int)(imagem.Height * perfil.EscalaY);

            camadaEsquerda.Mutate(x => x.Resize(novaLargura, novaAltura).GaussianBlur(0.8f));
            camadaDireita.Mutate(x => x.Resize(novaLargura, novaAltura).GaussianBlur(0.8f));

            // Aplica camadas com deslocamento (substitui o Translate)
            imagem.Mutate(ctx =>
            {
                // Camada expandida para a Esquerda
                ctx.DrawImage(
                    camadaEsquerda,
                    new Point(-perfil.DeslocamentoX, 0),
                    perfil.Opacidade * 0.5f);

                // Camada expandida para a Direita
                ctx.DrawImage(
                    camadaDireita,
                    new Point(perfil.DeslocamentoX, 0),
                    perfil.Opacidade * 0.5f);
            });
        }

        // Aplica ajuste fino de contraste e densidade nos pixels (com casts seguros)
        AjustarContrasteEDensidade(imagem, perfil);

        await imagem.SaveAsPngAsync(caminhoDestino, ct);

        logger.LogInformation("[VOLUME] Imagem salva com sucesso: {Destino}", caminhoDestino);
        return $"resultados/{nomeArquivo}";
    }

    private static void AjustarContrasteEDensidade(Image<Rgba32> imagem, VolumeProfile perfil)
    {
        float fatorEscurecimento = perfil.Nivel switch
        {
            4 => 0.94f,
            3 => 0.96f,
            2 => 0.98f,
            _ => 1.00f
        };

        imagem.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];

                    // Conversão explícita (byte) para evitar erro de compilação CS0266
                    byte r = (byte)Math.Clamp((int)(pixel.R * fatorEscurecimento), 0, 255);
                    byte g = (byte)Math.Clamp((int)(pixel.G * fatorEscurecimento), 0, 255);
                    byte b = (byte)Math.Clamp((int)(pixel.B * fatorEscurecimento), 0, 255);

                    pixel = new Rgba32(r, g, b, pixel.A);
                }
            }
        });
    }
}