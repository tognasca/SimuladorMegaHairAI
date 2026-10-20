using System.Linq;
using SimuladorMegaHair.Domain.Enums;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Prompts para FLUX Kontext (edição por instrução). Linguagem natural, positiva e específica.
public static class KontextPrompt
{
    public static string Build(string comprimento, string cor, string tipo, string metodo,
                               HairEditMode modo, bool foraDoQuadro = false)
    {
        var corEn = Cor(cor, out _);
        var tex = Textura(tipo);
        var (comp, local) = Comprimento(comprimento);

        // Se o comprimento pedido passa da borda inferior da foto, o modelo não vê onde "termina":
        // pedimos que o cabelo desça até a borda, com as pontas fora da foto.
        var fim = foraDoQuadro
            ? "reaching all the way down to the bottom edge of the photo, so the ends of the hair are out of frame"
            : $"ending {local}";

        var corFrase = $"The hair color is exactly {corEn}, evenly from roots to ends, not lighter and not darker. ";

        const string natural =
            "The hair must look like real natural human hair, worn loose and down, with no braids, " +
            "no ponytails, no pigtails, no buns, no bows, no clips and no accessories. ";
        const string keep =
            "Keep the person's face, facial hair, expression, skin, body, clothing, pose, lighting " +
            "and background exactly identical to the original photo.";

        return modo switch
        {
            HairEditMode.Recolor =>
                $"Change only the hair color of the person in this photo to {corEn}, evenly from roots to ends, " +
                "keeping the same hairstyle, length and texture. " + keep,

            // Diminuir: corta o cabelo no comprimento pedido e REMOVE o que passava dele,
            // mostrando a roupa, os ombros e o fundo que estavam atrás.
            HairEditMode.Shorten =>
                $"Cut the hair of the person in this photo shorter, into {corEn} hair with {tex}, {comp}, " +
                $"ending {local}. Remove all of the longer hair that hung below that length, so the shoulders, " +
                "clothing and background behind it are visible. " + corFrase + natural + keep,

            _ =>
                $"Edit only the hair of the person in this photo. Replace it with {corEn} hair with {tex}, {comp}, " +
                $"falling naturally over both shoulders and {fim}. The new hair grows naturally from " +
                "the person's existing hairline and roots, with realistic individual strands, natural volume and shine. " +
                corFrase + natural + keep
        };
    }

    /// A máscara do comprimento pedido vem PINTADA de verde na foto; o modelo troca o verde por cabelo real.
    public static string BuildGuiado(string cor, string tipo)
    {
        var corEn = Cor(cor, out _);
        var tex = Textura(tipo);
        const string natural =
            "The hair must look like real natural human hair, worn loose and down, with no braids, " +
            "no ponytails, no pigtails, no buns, no bows, no clips and no accessories. ";
        const string keep =
            "Keep the person's face, facial hair, expression, skin, body, clothing, pose, lighting " +
            "and background exactly identical to the original photo.";
        return
            "The bright green shape painted on this photo shows exactly where the person's new hair must be. " +
            $"Replace the green shape and the person's current hair with real, natural {corEn} hair with {tex}, " +
            "filling exactly the green area: from the top of the head, down both sides of the face, over both shoulders, " +
            "ending at the lower edge of the green shape. If the green shape reaches the bottom edge of the photo, " +
            "the hair continues to the bottom edge. No green color may remain anywhere in the image. " +
            $"The hair color is exactly {corEn}. " + natural + keep;
    }

    /// Prompt curto e positivo para o FLUX Fill (preenche só dentro da máscara).
    public static string BuildFill(string cor, string tipo)
    {
        var corEn = Cor(cor, out _);
        var tex = Textura(tipo);
        return $"{corEn} hair with {tex}, long natural hair flowing down over the shoulders and chest, " +
               "one continuous mass of hair covering the whole area, realistic strands and shine. Photograph.";
    }

    // ───────────────────────── COR ─────────────────────────
    // Sem padrão silencioso: se a cor não for reconhecida, o texto original vai para o prompt
    // e `mapeada` = false (o pipeline registra um aviso no log).
    public static string Cor(string? cor, out bool mapeada)
    {
        mapeada = true;
        var raw = (cor ?? "").Trim();
        if (raw.Length == 0) { mapeada = false; return "a natural color matching the existing hair"; }

        var c = SemAcento(raw).ToLowerInvariant();
        bool tem(params string[] ks) => ks.Any(k => c.Contains(k));
        bool loiro = tem("loiro", "loira", "louro", "loura", "blond");

        if (tem("platin", "polar", "ice", "branco", "white")) return "icy platinum blonde";

        if (loiro)
        {
            if (tem("medio", "medium")) return "medium natural blonde (dark blonde) with a warm golden-beige tone, clearly darker than platinum and not white";
            if (tem("escuro", "dark")) return "dark blonde with soft ash-brown undertones, not platinum";
            if (tem("claro", "light")) return "light blonde with a soft neutral tone";
            if (tem("dourad", "golden", "mel", "honey")) return "warm golden honey blonde";
            if (tem("cinza", "ash", "acinzentad")) return "ash blonde with cool tones";
            return "natural medium-light blonde";
        }

        if (tem("chocolate")) return "rich chocolate brown";
        if (tem("cafe", "coffee")) return "coffee brown";
        if (tem("castanh", "marrom", "brown"))
        {
            if (tem("escuro", "dark")) return "dark chestnut brown";
            if (tem("claro", "light")) return "light chestnut brown";
            return "medium chestnut brown";
        }
        if (tem("preto", "preta", "black", "azeviche")) return "jet black";
        if (tem("ruiv", "cobre", "acobreado", "ginger", "copper")) return "vibrant copper red";
        if (tem("borgonha", "vinho", "burgundy")) return "deep burgundy red";
        if (tem("vermelh", "red")) return "deep vivid red";
        if (tem("grisalh", "cinza", "prata", "silver", "gray", "grey")) return "silver gray";
        if (tem("rosa", "pink")) return "rose pink";
        if (tem("azul", "blue")) return "steel blue";
        if (tem("roxo", "lilas", "violet", "purple")) return "lavender purple";
        if (tem("verde", "green")) return "emerald green";
        if (tem("balayage", "ombre")) return "balayage with dark roots gradually blending into lighter ends";
        if (tem("iluminad", "luzes", "mechas", "highlight")) return "dark brown with soft caramel highlights";

        mapeada = false;
        return $"\"{raw}\" (a Brazilian hair color name)";
    }

    private static string SemAcento(string s)
    {
        var n = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string Textura(string? tipo)
    {
        var t = SemAcento(tipo ?? "").ToLowerInvariant();
        if (t.Contains("crespo") || t.Contains("afro") || t.Contains("coily")) return "tight coily curls";
        if (t.Contains("cach") || t.Contains("curly")) return "defined bouncy curls";
        if (t.Contains("ondul") || t.Contains("wavy")) return "soft natural waves";
        return "straight sleek texture";
    }

    /// Mapa de comprimento do salão (medido a partir da raiz, no corpo da cliente):
    /// 35/40 = acima do peito, quase no ombro; 45 = peito; 55 = abaixo do peito;
    /// 65 = umbigo; 85 = cintura.
    private static (string desc, string local) Comprimento(string? c)
    {
        var m = Regex.Match(c ?? "", @"\d+");
        int cm = m.Success && int.TryParse(m.Value, out var v) ? v : 60;
        return cm switch
        {
            <= 20 => ("chin-length", "at the chin"),
            <= 30 => ("shoulder-length", "at the shoulders"),
            <= 42 => ("almost shoulder-length", "just above the chest, close to the shoulders"),
            <= 50 => ("chest-length", "at the chest"),
            <= 60 => ("long", "just below the chest"),
            <= 75 => ("very long", "at the navel"),
            <= 90 => ("extra long, waist-length", "at the waist"),
            _ => ("extra long", "just below the waist")
        };
    }
}