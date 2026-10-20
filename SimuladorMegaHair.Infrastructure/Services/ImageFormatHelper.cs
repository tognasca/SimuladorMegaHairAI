namespace SimuladorMegaHair.Infrastructure.Services;

public static class ImageFormatHelper
{
    public static bool EhImagemValida(ReadOnlySpan<byte> cabecalho, string? nomeArquivo, out string extensao)
    {
        extensao = Path.GetExtension(nomeArquivo ?? "").ToLowerInvariant();

        if (EhJpeg(cabecalho))
        {
            extensao = extensao is ".jpg" or ".jpeg" ? extensao : ".jpg";
            return true;
        }

        if (EhPng(cabecalho))
        {
            extensao = ".png";
            return true;
        }

        if (EhWebp(cabecalho))
        {
            extensao = ".webp";
            return true;
        }

        return false;
    }

    private static bool EhJpeg(ReadOnlySpan<byte> b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool EhPng(ReadOnlySpan<byte> b) =>
        b.Length >= 8
        && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    private static bool EhWebp(ReadOnlySpan<byte> b) =>
        b.Length >= 12
        && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
        && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';
}
