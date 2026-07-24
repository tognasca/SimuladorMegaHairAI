namespace SimuladorMegaHair.Infrastructure.Services;

public static class PromptBuilder
{
    public static string Build(
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair)
    {
        var corIngles = TraduzirCor(cor);
        var tipoIngles = TraduzirTipo(tipoCabelo);
        var comprimentoIngles = TraduzirComprimento(comprimento);

        // Prompt neutro, sem palavras que ativam filtro NSFW
        return $"portrait photo of a person wearing modest clothing, " +
               $"brown colored straight hair, shoulder length, " +
               $"natural hair texture, realistic hair strands, " +
               $"studio portrait, professional headshot, family friendly, " +
               $"conservative style, wholesome appearance";
    }

    private static string TraduzirCor(string cor) => cor?.ToLowerInvariant() switch
    {
        "preto" => "black colored",
        "castanho" => "brown colored",
        "chocolate" => "dark brown colored",
        "loiro" => "blonde colored",
        "mel" => "honey blonde colored",
        _ => cor?.ToLowerInvariant() ?? "brown"
    };

    private static string TraduzirTipo(string tipo) => tipo?.ToLowerInvariant() switch
    {
        "liso" => "straight",
        "ondulado" => "wavy",
        "cacheado" => "curly",
        _ => tipo?.ToLowerInvariant() ?? "straight"
    };

    private static string TraduzirComprimento(string comprimento) => comprimento?.ToLowerInvariant() switch
    {
        "45 cm" => "shoulder length",
        "55 cm" => "chest length",
        "65 cm" => "long",
        "75 cm" => "very long",
        "85 cm" => "extra long",
        _ => "long"
    };
}