namespace SimuladorMegaHair.Infrastructure.Storage;

public enum FormatoImagem { Jpeg, Png, Webp }

public sealed record InfoImagem(FormatoImagem Formato, int Largura, int Altura)
{
    /// <summary>Extensão canônica (minúscula) do formato REAL detectado.</summary>
    public string Extensao => Formato switch
    {
        FormatoImagem.Jpeg => ".jpg",
        FormatoImagem.Png => ".png",
        _ => ".webp"
    };
}

/// <summary>
/// Identifica o formato REAL de uma imagem pelos primeiros bytes (assinatura
/// "magic bytes") e lê a largura/altura do cabeçalho, sem decodificar os
/// pixels e sem depender de nenhuma biblioteca.
///
/// POR QUE EXISTE: o upload só conferia a extensão do nome do arquivo (que o
/// cliente escolhe livremente), e não havia limite de resolução: uma imagem
/// pequena em bytes pode declarar dezenas de milhares de pixels de lado e
/// esgotar a memória ao ser decodificada ("decompression bomb").
/// </summary>
public static class ImagemInspetor
{
    private const int BytesMaximosLidos = 1024 * 1024; // cabeçalho/EXIF cabem folgados

    public static async Task<InfoImagem?> InspecionarAsync(
        Stream stream, CancellationToken ct = default)
    {
        var buffer = new byte[BytesMaximosLidos];
        var total = 0;

        while (total < buffer.Length)
        {
            var lidos = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (lidos == 0) break;
            total += lidos;
        }

        return Inspecionar(buffer.AsSpan(0, total));
    }

    public static InfoImagem? Inspecionar(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 &&
            d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 &&
            d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
            return LerPng(d);

        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return LerJpeg(d);

        if (d.Length >= 30 &&
            d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' &&
            d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
            return LerWebp(d);

        return null;
    }

    /// <summary>
    /// Devolve o motivo (texto para o usuário) se a imagem violar os limites,
    /// ou null se estiver dentro deles.
    /// </summary>
    public static string? MotivoRejeicao(InfoImagem info, int minLado, int maxLado, long maxPixels)
    {
        if (info.Largura < minLado || info.Altura < minLado)
            return $"A imagem é muito pequena (mínimo {minLado} px de lado).";

        if (info.Largura > maxLado || info.Altura > maxLado ||
            (long)info.Largura * info.Altura > maxPixels)
            return "A imagem tem resolução acima do permitido.";

        return null;
    }

    // ── PNG: assinatura(8) + chunk IHDR: len(4) "IHDR"(4) largura(4) altura(4)
    private static InfoImagem? LerPng(ReadOnlySpan<byte> d)
    {
        if (d[12] != 'I' || d[13] != 'H' || d[14] != 'D' || d[15] != 'R')
            return null;

        int w = BigEndian32(d, 16);
        int h = BigEndian32(d, 20);
        return w > 0 && h > 0 ? new InfoImagem(FormatoImagem.Png, w, h) : null;
    }

    // ── JPEG: percorre os segmentos até achar um SOF (Start Of Frame)
    private static InfoImagem? LerJpeg(ReadOnlySpan<byte> d)
    {
        var pos = 2;

        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0xFF) return null;

            while (pos < d.Length && d[pos] == 0xFF) pos++; // bytes de preenchimento
            if (pos >= d.Length) return null;

            var marcador = d[pos++];

            // marcadores sem corpo
            if (marcador == 0xD8 || marcador == 0x01 || (marcador >= 0xD0 && marcador <= 0xD7))
                continue;

            // fim da imagem ou início dos dados sem ter achado o SOF
            if (marcador == 0xD9 || marcador == 0xDA) return null;

            if (pos + 2 > d.Length) return null;
            int len = (d[pos] << 8) | d[pos + 1];
            if (len < 2) return null;

            var ehSof = marcador >= 0xC0 && marcador <= 0xCF &&
                        marcador != 0xC4 && marcador != 0xC8 && marcador != 0xCC;

            if (ehSof)
            {
                // len(2) precisão(1) altura(2) largura(2)
                if (pos + 7 > d.Length) return null;
                int h = (d[pos + 3] << 8) | d[pos + 4];
                int w = (d[pos + 5] << 8) | d[pos + 6];
                return w > 0 && h > 0 ? new InfoImagem(FormatoImagem.Jpeg, w, h) : null;
            }

            pos += len;
        }

        return null;
    }

    // ── WEBP: "RIFF" tam "WEBP" + chunk VP8 (com perdas), VP8L (sem perdas) ou VP8X
    private static InfoImagem? LerWebp(ReadOnlySpan<byte> d)
    {
        if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == 'X')
        {
            int w = 1 + (d[24] | (d[25] << 8) | (d[26] << 16));
            int h = 1 + (d[27] | (d[28] << 8) | (d[29] << 16));
            return new InfoImagem(FormatoImagem.Webp, w, h);
        }

        if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == ' ')
        {
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A) return null;
            int w = (d[26] | (d[27] << 8)) & 0x3FFF;
            int h = (d[28] | (d[29] << 8)) & 0x3FFF;
            return w > 0 && h > 0 ? new InfoImagem(FormatoImagem.Webp, w, h) : null;
        }

        if (d[12] == 'V' && d[13] == 'P' && d[14] == '8' && d[15] == 'L')
        {
            if (d[20] != 0x2F) return null;
            uint bits = (uint)(d[21] | (d[22] << 8) | (d[23] << 16)) | ((uint)d[24] << 24);
            int w = (int)(bits & 0x3FFF) + 1;
            int h = (int)((bits >> 14) & 0x3FFF) + 1;
            return new InfoImagem(FormatoImagem.Webp, w, h);
        }

        return null;
    }

    private static int BigEndian32(ReadOnlySpan<byte> d, int o) =>
        (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
}
