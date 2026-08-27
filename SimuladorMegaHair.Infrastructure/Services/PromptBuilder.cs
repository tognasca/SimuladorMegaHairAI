using SimuladorMegaHair.Domain.Enums;
using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Infrastructure.Services;

public static class PromptBuilder
{
    /// <summary>
    /// Gera prompt principal para inpainting FLUX Fill.
    /// Prioriza vocabulário feminino de salão de beleza.
    /// </summary>
    /// 

    public static string BuildInstrucao(string comprimento, string cor, string tipo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipo);
        var compEn = TraduzirComprimentoFeminino(comprimento);

        return
            $"Replace only the hair with {compEn} {corEn} {tipoEn} hair extensions, " +
            $"full and voluminous, falling in front of both shoulders onto the chest. " +
            $"Keep the face, skin, makeup, expression, glasses, clothing, neckline, " +
            $"body shape, background and lighting exactly the same. " +
            $"Do not change the outfit. Do not add cleavage. Photorealistic.";
    }

    public static string BuildInpainting(
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodo,
        HairEditMode modo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);
        var compEn = TraduzirComprimentoFeminino(comprimento);
        var metodoEn = TraduzirMetodo(metodo);

        if (modo == HairEditMode.Extend || modo == HairEditMode.Recolor)
        {
            return
                "same woman, identical face, identical skin, identical makeup, " +
    "KEEP ORIGINAL SHIRT AND BODY SHAPE UNCHANGED, no larger breasts, no deeper cleavage, no lingerie, " +
    $"{compEn} {corEn} {tipoEn} hair extensions, " +
    "REPLACE ALL EXISTING HAIR completely, " +
    "long hair draped OVER the front of both shoulders, hair in front of shoulders, " +
    "hair covering shoulder tops, not hidden behind back, " +
    "full voluminous salon mega hair, photorealistic, seamless hairline";
        }

        if (modo == HairEditMode.Shorten)
        {
            return
                $"same woman, same face, " +
                $"{corEn} hair, {compEn} cut, " +
                $"trendy {corEn} {tipoEn} haircut, " +
                $"modern silhouette, clean ends, frame the face, " +
                $"salon quality, photorealistic";
        }

        // Recolor
        return
            $"same woman, same face, same hairstyle length, " +
            $"recolor hair to vibrant {corEn}, " +
            $"uniform {corEn} from roots to tips, " +
            $"glossy healthy {tipoEn} texture, dimension, shine";
    }

    public static IEnumerable<(string prompt, string negative)> BuildFallbacks(
        string comprimento, string cor, string tipoCabelo, string metodo, HairEditMode modo)
    {
        yield return (BuildInpainting(comprimento, cor, tipoCabelo, metodo, modo),
                      BuildNegative(cor));

        // Fallback 2: Estilo alternativo (mais descritivo para a IA "recuperar")
        yield return (
            $"beautiful woman with {TraduzirComprimentoFeminino(comprimento)} {TraduzirCor(cor)} {TraduzirTipo(tipoCabelo)} hair, " +
            $"{TraduzirMetodo(metodo)}, flowing over shoulders, luxury beauty photo",
            BuildNegative(cor));

        // Fallback 3: Foco em "seamless integration" (caso o primeiro altere rosto)
        yield return (
            $"preserve identity exactly, only change hair to {TraduzirCor(cor)} {TraduzirTipo(tipoCabelo)}, " +
            $"{TraduzirComprimentoFeminino(comprimento)}, extensions, seamless blend with scalp",
            BuildNegative(cor));
    }

    public static string BuildNegative(string corDesejada)
    {
        var corNorm = corDesejada?.ToLowerInvariant() ?? "";

        // Se pedido loiro/platinado: PROIBIR amarelo
        bool bloqueiaAmarelo = corNorm.Contains("loiro") ||
                              corNorm.Contains("blonde") ||
                              corNorm.Contains("platinado");

        string antiAmarelo = bloqueiaAmarelo
            ? "yellow hair, orange hair, golden yellow, carrot color, brassy tones, highlight streaks only, "
            : "";

        string antiCor = "";

        if (corNorm.Contains("loiro") || corNorm.Contains("platinado") || corNorm.Contains("claro"))
            antiCor = "black hair, dark brown hair, yellow hair, orange hair, ";
        else if (corNorm.Contains("preto") || corNorm.Contains("castanho") || corNorm.Contains("chocolate"))
            antiCor = "blonde hair, platinum, ";

        return
            "hair behind back only, hair tucked behind shoulders, leftover old hair, " +
"cleavage, bare breasts, bigger breasts, boob job, nude, low-cut, " +
"different body, different clothes, face change, " +
            "cleavage, low cut top, naked chest, bare breasts, changed clothing, bra, swimsuit, underwear, " + // 🔒 NEGAÇÃO RÍGIDA DE DECOTE
            "short buzzcut, bald, altered facial features, different person, distorted face, cartoon, low resolution";
    }

    // ─── TRADUTORES OTIMIZADOS PARA FEMININO ───

    public static string TraduzirComprimentoFeminino(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return "medium-length";

        var texto = c.ToLowerInvariant().Trim();
        var match = Regex.Match(texto, @"\d+");

        if (match.Success && int.TryParse(match.Value, out int cm))
        {
            return cm switch
            {
                <= 25 => "very short pixie cut",
                <= 35 => "short bob haircut",
                <= 45 => "medium shoulder-length",      // Popular! Lob
                <= 55 => "medium-long past shoulders",   // Bra-strap
                <= 65 => "long mid-back length",
                <= 75 => "extra-long waist-length",
                _ => "super long floor-length"       // Extremos
            };
        }

        if (texto.Contains("curto")) return "short bob";
        if (texto.Contains("medio") || texto.Contains("médio")) return "medium lob";
        if (texto.Contains("longo") || texto.Contains("85") || texto.Contains("mega")) return "long waist-length";

        return "medium-length"; // seguro padrão feminino
    }

    public static string TraduzirCor(string cor)
    {
        if (string.IsNullOrWhiteSpace(cor)) return "platinum ash blonde"; // PADRÃO MUDOU!

        var c = cor.ToLowerInvariant().Trim();

        // LOIROS: Priorizar tons FRIOS (Platinado/Ash) ao invés de Dourados
        if (c.Contains("platinado") || c.Contains("platina") || c.Contains("ice") || c.Contains("polar"))
            return "icy platinum blonde";

        if (c.Contains("loiro") || c.Contains("loira") || c.Contains("blonde"))
        {
            // Se especificou DOURADO, permite golden. Senão, Ash default
            if (c.Contains("dourado") || c.Contains("golden") || c.Contains("mel"))
                return "warm golden honey blonde";

            // PADRÃO: Loiro Brasileiro = Ash Platinado (não amarelado!)
            return "platinum ash blonde with cool tones";
        }
        // Morenas
        if (c.Contains("chocolate"))
            return "rich chocolate brown";
        if (c.Contains("castanho"))
        {
            if (c.Contains("claro") || c.Contains("acastanhado")) return "light chestnut brown";
            return "medium chestnut brown";
        }
        if (c.Contains("preto") || c.Contains("black"))
            return "jet black";

        // Ruivas/Vermelhos (muito usados em mega hair para destaque)
        if (c.Contains("ruivo") || c.Contains("vermelho") || c.Contains("ginger"))
            return "vibrant copper red";
        if (c.Contains("acobreado") || c.Contains("auburn"))
            return "deep auburn red";

        // Fantasias/Tendências
        if (c.Contains("rosa") || c.Contains("pink"))
            return "rose gold pink";
        if (c.Contains("azul") || c.Contains("blue"))
            return "steel blue";
        if (c.Contains("roxo") || c.Contains("purple"))
            return "lavender purple";
        if (c.Contains("cinza") || c.Contains("gray"))
            return "silver gray";

        // Iluminação / Reflexos
        if (c.Contains("iluminado") || c.Contains("luzes") || c.Contains("mechas"))
            return "dark brown with caramel highlights";
        if (c.Contains("balayage") || c.Contains("ombre"))
            return "balayage ombre effect dark to light";

        return "honey blonde"; // default seguro e desejado
    }

    public static string TraduzirTipo(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "straight silky";

        var t = tipo.ToLowerInvariant().Trim();

        if (t.Contains("cacheado") || t.Contains("cacho") || t.Contains("curly"))
            return "bouncy curly";
        if (t.Contains("ondulado") || t.Contains("wavy"))
            return "soft beach waves";
        if (t.Contains("liso") || t.Contains("straight"))
            return "pin straight sleek";
        if (t.Contains("crespo") || t.Contains("coily"))
            return "tight coily texture";
        if (t.Contains("afro") || t.Contains("kinky"))
            return "afro kinky curls";
        if (t.Contains("vozinho") || t.Contains("volume"))
            return "big voluminous blowout style";

        return "straight silky";
    }

    public static string TraduzirMetodo(string metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo)) return "tape-in extensions";

        var m = metodo.ToLowerInvariant().Trim();

        if (m.Contains("fita") || m.Contains("tape"))
            return "tape-in hair extensions";
        if (m.Contains("capsula") || m.Contains("micro") || m.Contains("microlink"))
            return "micro-bead extensions";
        if (m.Contains("queratina") || m.Contains("bond"))
            return "keratin bond extensions";
        if (m.Contains("crochet") || m.Contains("trança"))
            return "crochet braids extensions";
        if (m.Contains("nano") || m.Contains("nanoring"))
            return "nano ring extensions";

        return "tape-in hair extensions"; // método mais comum para mega hair brasileiro
    }
}