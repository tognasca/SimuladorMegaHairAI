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
        return BuildPromptBase(comprimento, cor, tipo, "", HairEditMode.Extend);
    }

    //public static string BuildInpainting(
    //    string comprimento,
    //    string cor,
    //    string tipoCabelo,
    //    string metodo,
    //    HairEditMode modo)
    //{
    //    return BuildPromptBase(comprimento, cor, tipoCabelo, metodo, modo);
    //}

    private static (string descricao, string local) ResolverComprimento(string comprimento)
    {
        var match = Regex.Match(comprimento ?? string.Empty, @"\d+");

        if (match.Success && int.TryParse(match.Value, out var cm))
        {
            return cm switch
            {
                <= 25 => ("very short pixie cut", "to the jawline"),
                <= 35 => ("short bob", "to the chin"),
                <= 45 => ("shoulder-length", "to the shoulders"),
                <= 55 => ("medium-long", "past the shoulders to the upper chest"),
                <= 65 => ("long", "down to mid-back"),
                <= 75 => ("extra-long", "down to the waist"),
                <= 90 => ("very long", "down to the hips"),
                _ => ("extra-long", "down past the hips")
            };
        }

        var t = (comprimento ?? "").ToLowerInvariant();
        if (t.Contains("curto")) return ("short bob", "to the chin");
        if (t.Contains("medio") || t.Contains("médio")) return ("shoulder-length", "to the shoulders");
        if (t.Contains("longo") || t.Contains("mega")) return ("long", "down to the waist");

        return ("medium-length", "to the shoulders");
    }
    public static string BuildInpainting(string comprimento, string cor, string tipoCabelo,
                                      string metodo, HairEditMode modo)
    {
        var corEn = TraduzirCor(cor);
        var tipoEn = TraduzirTipo(tipoCabelo);
        var (compEn, local) = ResolverComprimento(comprimento);

        return modo switch
        {
            HairEditMode.Recolor =>
                $"Natural {corEn} hair, same style and length, evenly colored from roots to ends, " +
                "realistic strands and lighting. Photograph.",

            HairEditMode.Shorten =>
                $"Natural {corEn} {tipoEn} hair, {compEn}, neatly cut with natural ends. Photograph.",

            _ =>  $"{corEn} {tipoEn} hair, {compEn}, all hair evenly {corEn} from roots to ends, " +
                  $"flowing down both sides {local}. Photograph."
        };
    }
    private static string BuildPromptBase(
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
        var comprimentoCmMatch = Regex.Match(comprimento ?? string.Empty, @"\d+");
        var comprimentoCm = comprimentoCmMatch.Success && int.TryParse(comprimentoCmMatch.Value, out var cm)
            ? cm
            : 60;
        var orientacaoComprimento = comprimentoCm switch
        {
            <= 45 => "The ends should fall around shoulder level, according to the requested length. ",
            <= 55 => "The ends should fall around the upper chest, according to the requested length. ",
            <= 65 => "The ends should fall around mid-back level, according to the requested length. ",
            <= 75 => "The ends should fall around waist level, according to the requested length. ",
            <= 90 => "The ends should fall around hip level, according to the requested length, not to the floor. ",
            _ => "The ends should fall below hip level in proportion to the requested length, not unnaturally to the floor. "
        };

        const string preserve =
            "Perform a localized edit on the supplied photograph. This is an image-editing task, NOT a scene-generation task. " +
            "Preserve the original image and every existing person and object exactly as photographed. " +
            "Do not add, remove, replace, duplicate, or reposition any person. Do not generate a new face, body, " +
            "head, salon, background, or secondary subject. Edit only the hair pixels inside the provided mask. ";

        const string hairstyle =
            "Continue the existing hair naturally from the visible scalp and roots. Hair is loose and worn down, " +
            "with natural strand direction, realistic individual strands, believable density, gravity, and lighting. ";

        const string avoid =
            "No new people, no additional heads or faces, no scene changes, no hairstyle accessories, " +
            "no ponytail, pigtails, buns, braids, or updos unless explicitly requested. " +
            "Do not alter facial features, skin, expression, clothing, accessories, body, or background. ";

        if (modo == HairEditMode.Shorten)
        {
            return preserve +
                $"Change only the hair to a realistic {corEn} {tipoEn} haircut, {compEn}. " +
                "Hair remains loose and down, with a modern salon shape and natural ends. " + avoid +
                "Photorealistic result matching the original photograph.";
        }

        if (modo == HairEditMode.Recolor)
        {
            return preserve + hairstyle +
                $"Keep the existing loose hairstyle and its approximate length and silhouette. " +
                $"Recolor the hair only to {corEn}, with consistent color from roots to ends and realistic highlights. " +
                avoid + "Photorealistic salon result.";
        }

        return preserve + hairstyle +
            $"Extend the existing hair with {corEn} hair extensions, {tipoEn} texture, {compEn}. " +
            orientacaoComprimento +
            (string.IsNullOrWhiteSpace(metodoEn) ? "" : $"The requested extension application method is {metodoEn}; represent its attachment naturally and discreetly, without exposing artificial seams. ") +
            "Match the requested texture exactly; do not impose waves on straight, curly, or coily hair. " +
            "Blend extensions seamlessly into the existing roots; natural layered strands, realistic shine and moderate salon volume. " +
            avoid + "Photorealistic hair-only edit. The output must remain the same original photograph, not a newly generated scene.";
    }

    public static IEnumerable<(string prompt, string negative)> BuildFallbacks(
        string comprimento, string cor, string tipoCabelo, string metodo, HairEditMode modo)
    {
        var primary = BuildInpainting(comprimento, cor, tipoCabelo, metodo, modo);
        var negative = BuildNegative(cor);

        // Cada alternativa preserva as mesmas restrições. Nunca usar um fallback
        // genérico de "beautiful woman", pois isso incentiva a recriação da pessoa
        // e perde os requisitos do megahair.
        yield return (primary, negative);
        yield return (
            "Edit only the hair inside the supplied mask in the exact input photograph. Do not create or alter any person, face, object, or background. " +
            $"Extend the existing hair naturally using {TraduzirCor(cor)} hair, {TraduzirTipo(tipoCabelo)} texture, " +
            $"{TraduzirComprimentoFeminino(comprimento)}. Preserve every non-hair region unchanged. Photorealistic hair-only edit.",
            negative);
        yield return (
            "Strict masked inpainting of the supplied photograph: modify hair only, within the supplied mask. " +
            "Do not generate a woman, face, body, second person, salon, or any new scene content. " +
            $"Continue the existing roots into {TraduzirCor(cor)} {TraduzirTipo(tipoCabelo)} hair, " +
            $"{TraduzirComprimentoFeminino(comprimento)}. Keep all non-hair pixels unchanged.",
            negative);
    }

    public static string BuildNegative(string corDesejada)
    {
        var corNorm = corDesejada?.ToLowerInvariant() ?? "";
        var antiAmarelo = (corNorm.Contains("loiro") || corNorm.Contains("blonde") || corNorm.Contains("platinado"))
            ? "brassy yellow, orange hair, carrot tones, overly golden yellow hair, "
            : "";

        return antiAmarelo +
            "pigtails, twin tails, two side ponytails, ponytail, bun, double bun, braid, updo, tied hair, " +
            "hair bow, ribbon, hair clip, childish hairstyle, artificial wig hairline, plastic hair, " +
            "different person, altered face, altered skin, changed expression, changed clothes, changed body, " +
            "cleavage, lingerie, nudity, cartoon, blurry image, low resolution";
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
                <= 90 => "very long hip-length",
                _ => "extra-long lower-hip length"
            };
        }

        if (texto.Contains("curto")) return "short bob";
        if (texto.Contains("medio") || texto.Contains("médio")) return "medium lob";
        if (texto.Contains("longo") || texto.Contains("85") || texto.Contains("mega")) return "long waist-length";

        return "medium-length"; // seguro padrão feminino
    }

    public static string TraduzirCor(string cor)
    {
        if (string.IsNullOrWhiteSpace(cor)) return "a natural color matching the existing hair";

        var c = cor.ToLowerInvariant().Trim();

        // LOIROS: Priorizar tons FRIOS (Platinado/Ash) ao invés de Dourados
        if (c.Contains("platinado") || c.Contains("platina") || c.Contains("ice") || c.Contains("polar"))
            return "icy platinum blonde";

        if ((c.Contains("loiro") || c.Contains("loira") || c.Contains("blonde")) && c.Contains("claro"))
            return "light beige blonde with neutral-cool undertones";

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

        return "a natural color matching the existing hair"; // não inventar cor quando a opção é desconhecida
    }

    public static string TraduzirTipo(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "straight silky";

        var t = tipo.ToLowerInvariant().Trim();

        if (t.Contains("cacheado") || t.Contains("cacho") || t.Contains("curly"))
            return "thick defined voluminous curls";
        if (t.Contains("ondulado") || t.Contains("wavy"))
            return "natural loose S-shaped waves, flowing downward from the roots, " +
                   "with realistic strand separation and soft waves along the lengths";
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
        if (string.IsNullOrWhiteSpace(metodo)) return "tape-in hair extensions";

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