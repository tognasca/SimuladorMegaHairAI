using Microsoft.Extensions.Logging;
using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// <summary>
/// Aplica ganho de volume/densidade na imagem já gerada pela IA.
///
/// IMPORTANTE: todo o efeito é restrito à MÁSCARA DE CABELO (gerada pelo
/// HairMaskGenerator). Isso corrige o problema da versão anterior, que
/// deslocava/borrava a imagem inteira (rosto e fundo inclusos), gerando
/// um efeito de "fantasma". Agora só os fios ganham densidade extra.
/// </summary>
public static class HairVolumeAdjuster
{
    public record VolumeProfile(
        int Nivel,
        int NivelGramas,
        int DeslocamentoX,
        float Opacidade,
        float BlurSigma
    );

    // Perfis de volume calibrados para cada nível de Mega Hair.
    // DeslocamentoX = o quanto a "camada de fios extra" é puxada para os
    // lados a partir da máscara original, dando sensação de mais densidade
    // nas laterais (efeito solicitado), sem afetar rosto/fundo.
    private static readonly Dictionary<int, VolumeProfile> Perfis = new()
    {
        [1] = new(1, 100, DeslocamentoX: 5, Opacidade: 0.35f, BlurSigma: 2.2f),
        [2] = new(2, 200, DeslocamentoX: 9, Opacidade: 0.50f, BlurSigma: 2.6f),
        [3] = new(3, 300, DeslocamentoX: 14, Opacidade: 0.65f, BlurSigma: 3.0f),
        [4] = new(4, 400, DeslocamentoX: 20, Opacidade: 0.80f, BlurSigma: 3.4f)
    };

    /// <summary>
    /// Aplica o ganho de volume usando uma máscara de cabelo para restringir
    /// o efeito. Se nenhuma máscara for informada (ou o arquivo não existir),
    /// cai para um modo de segurança (elipse central) bem mais sutil.
    /// </summary>
    public static async Task<string> Aplicar(
        AjustarVolumeRequest req,
        string? maskPath,
        ILogger logger,
        string pastaSaida,
        CancellationToken ct = default)
    {
        if (!Perfis.TryGetValue(req.Nivel, out var perfil))
        {
            perfil = Perfis[2];
        }

        logger.LogInformation(
            "[VOLUME] Processando volume Nível {Nivel} ({Gramas}g) — máscara: {TemMascara}...",
            perfil.Nivel, perfil.NivelGramas, !string.IsNullOrWhiteSpace(maskPath));

        var caminhoOrigem = req.ImagemResultadoPath ?? req.ImagemOriginalPath;
        if (string.IsNullOrWhiteSpace(caminhoOrigem) || !File.Exists(caminhoOrigem))
        {
            throw new FileNotFoundException("A imagem para ajuste de volume não foi encontrada.", caminhoOrigem);
        }

        var nomeArquivo = $"volume_{perfil.NivelGramas}g_{Guid.NewGuid():N}.png";
        var caminhoDestino = Path.Combine(pastaSaida, nomeArquivo);

        using var imagem = await Image.LoadAsync<Rgba32>(caminhoOrigem, ct);

        // Extrai a máscara para uma matriz simples de bytes (0-255).
        // Isso evita ter que reabrir o Image<L8> pixel a pixel dentro do
        // loop principal e deixa o código mais simples e seguro.
        var pesos = await CarregarPesosMascaraAsync(maskPath, imagem.Size, ct);

        // 1) Camada contendo SOMENTE os pixels do cabelo (alpha = peso da
        //    máscara). Fora da região de cabelo, alpha = 0 → totalmente
        //    transparente, nunca pinta rosto/pele/fundo.
        using var camadaFios = ConstruirCamadaDeFios(imagem, pesos);

        // 2) Suaviza para criar um "halo" de fios soltos nas bordas do
        //    cabelo — é isso que dá a sensação de mais densidade/volume.
        camadaFios.Mutate(x => x.GaussianBlur(perfil.BlurSigma));

        // 3) Desenha a camada deslocada para a esquerda e para a direita.
        //    Como a camada já carrega seu próprio alpha (mascarado +
        //    borrado), o ImageSharp faz o alpha-blend respeitando essa
        //    transparência — o efeito nunca sai da área de cabelo original
        //    (mais o halo suave do blur), preservando rosto e fundo intactos.
        imagem.Mutate(ctx =>
        {
            ctx.DrawImage(camadaFios, new Point(-perfil.DeslocamentoX, 0), perfil.Opacidade);
            ctx.DrawImage(camadaFios, new Point(perfil.DeslocamentoX, 0), perfil.Opacidade);

            // Ganho leve para cima, dando volume também na raiz/topo
            // (mais perceptível a partir do nível 3).
            if (perfil.Nivel >= 3)
            {
                ctx.DrawImage(camadaFios, new Point(0, -perfil.DeslocamentoX / 2), perfil.Opacidade * 0.5f);
            }
        });

        // 4) Densidade: escurece levemente SÓ dentro da máscara original
        //    (não borrada), proporcional ao peso em cada pixel — dá aspecto
        //    de cabelo mais cheio sem tocar em nada fora da região de cabelo.
        AjustarDensidade(imagem, pesos, perfil);

        await imagem.SaveAsPngAsync(caminhoDestino, ct);

        logger.LogInformation("[VOLUME] Imagem salva com sucesso: {Destino}", caminhoDestino);
        return $"resultados/{nomeArquivo}";
    }

    /// <summary>
    /// Carrega a máscara de cabelo (se existir) como uma matriz [altura][largura]
    /// de pesos 0-255. Se não houver máscara, retorna null (o chamador usa o
    /// fallback de elipse central).
    /// </summary>
    private static async Task<byte[,]?> CarregarPesosMascaraAsync(
        string? maskPath, Size tamanhoAlvo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(maskPath) || !File.Exists(maskPath))
            return null;

        using var mascara = await Image.LoadAsync<L8>(maskPath, ct);

        if (mascara.Size != tamanhoAlvo)
        {
            mascara.Mutate(x => x.Resize(tamanhoAlvo));
        }

        var pesos = new byte[tamanhoAlvo.Height, tamanhoAlvo.Width];

        mascara.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var linha = accessor.GetRowSpan(y);
                for (int x = 0; x < linha.Length; x++)
                {
                    pesos[y, x] = linha[x].PackedValue;
                }
            }
        });

        return pesos;
    }

    private static byte PesoNoPixel(byte[,]? pesos, int x, int y, int w, int h)
        => pesos is not null ? pesos[y, x] : FallbackElipseAlpha(x, y, w, h);

    /// <summary>
    /// Constrói uma camada RGBA do mesmo tamanho da imagem, copiando a cor
    /// original apenas onde há cabelo (segundo os pesos) e usando o peso
    /// como canal alpha.
    /// </summary>
    private static Image<Rgba32> ConstruirCamadaDeFios(Image<Rgba32> imagem, byte[,]? pesos)
    {
        var camada = imagem.Clone();
        int w = camada.Width, h = camada.Height;

        camada.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var linha = accessor.GetRowSpan(y);
                for (int x = 0; x < linha.Length; x++)
                {
                    byte alpha = PesoNoPixel(pesos, x, y, w, h);
                    ref var pixel = ref linha[x];
                    pixel = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                }
            }
        });

        return camada;
    }

    private static byte FallbackElipseAlpha(int x, int y, int w, int h)
    {
        float cx = w * 0.5f, cy = h * 0.42f;
        float rx = w * 0.42f, ry = h * 0.55f;
        float dx = (x - cx) / rx, dy = (y - cy) / ry;
        float dist = dx * dx + dy * dy;

        if (dist >= 1f) return 0;
        float t = 1f - dist; // feather suave da borda para o centro
        return (byte)Math.Clamp(t * 255f, 0, 200); // limita o pico p/ não ficar forte demais
    }

    private static void AjustarDensidade(Image<Rgba32> imagem, byte[,]? pesos, VolumeProfile perfil)
    {
        float fatorMax = perfil.Nivel switch
        {
            4 => 0.90f,
            3 => 0.93f,
            2 => 0.96f,
            _ => 0.98f
        };

        int w = imagem.Width, h = imagem.Height;

        imagem.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var linha = accessor.GetRowSpan(y);
                for (int x = 0; x < linha.Length; x++)
                {
                    float peso = PesoNoPixel(pesos, x, y, w, h) / 255f;
                    if (peso <= 0.01f) continue; // fora do cabelo: não mexe

                    // Interpola entre 1.0 (sem alteração) e fatorMax,
                    // proporcional à intensidade do peso nesse pixel.
                    float fator = 1f - (1f - fatorMax) * peso;

                    ref var pixel = ref linha[x];
                    byte r = (byte)Math.Clamp((int)(pixel.R * fator), 0, 255);
                    byte g = (byte)Math.Clamp((int)(pixel.G * fator), 0, 255);
                    byte b = (byte)Math.Clamp((int)(pixel.B * fator), 0, 255);
                    pixel = new Rgba32(r, g, b, pixel.A);
                }
            }
        });
    }
}
