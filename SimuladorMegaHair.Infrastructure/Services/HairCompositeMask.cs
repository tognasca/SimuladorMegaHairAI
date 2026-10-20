using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Máscara final do composite = (cabelo do ORIGINAL ∪ cabelo do RESULTADO) ∩ zona permitida.
/// Só pixels de cabelo vêm do modelo; corpo, roupa, mãos e fundo vêm sempre da foto original.
public static class HairCompositeMask
{
    public static Image<L8> Build(Image<L8> hairOld, Image<L8> hairNew, Image<L8> zona,
                                  float dilateSigma, out double frac)
    {
        int w = zona.Width, h = zona.Height;
        if (hairOld.Width != w || hairOld.Height != h) hairOld.Mutate(x => x.Resize(w, h));
        if (hairNew.Width != w || hairNew.Height != h) hairNew.Mutate(x => x.Resize(w, h));

        var u = new Image<L8>(w, h, new L8(0));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (hairOld[x, y].PackedValue > 127 || hairNew[x, y].PackedValue > 127)
                    u[x, y] = new L8(255);

        // dilatação leve: cobre fios soltos e bordas
        u.Mutate(c => c.GaussianBlur(Math.Max(1.5f, dilateSigma)));
        u.Mutate(c => c.BinaryThreshold(0.08f));

        // zona permitida (já exclui rosto e pescoço)
        long white = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (zona[x, y].PackedValue <= 127) u[x, y] = new L8(0);
                else if (u[x, y].PackedValue > 127) white++;
            }
        frac = white / (double)(w * h);

        u.Mutate(c => c.GaussianBlur(2f));
        return u;
    }
}