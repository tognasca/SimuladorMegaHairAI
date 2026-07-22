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

        return $"""
            TASK: Hair extension simulation - ONLY modify the hair region.

            ═══════════════════════════════════════════════════
            ABSOLUTE PRESERVATION RULES (MUST NOT BE VIOLATED):
            ═══════════════════════════════════════════════════
            
            PRESERVE 100% IDENTICAL (do not touch, do not regenerate):
            • The exact same face (all facial features must be pixel-perfect identical)
            • Eyes: same shape, color, position, gaze direction
            • Nose: same shape, size, and position
            • Mouth and lips: same shape, expression, and color
            • Eyebrows: same shape, thickness, and color
            • Facial hair (beard, mustache, stubble): keep IDENTICAL
            • Skin: same tone, texture, pores, freckles, marks, wrinkles
            • Ears: same shape and position
            • Jawline and face shape: unchanged
            • Neck: unchanged
            • Facial expression: identical
            • Age and gender appearance: identical
            • Glasses, earrings, piercings: keep exactly as they are
            • Clothing and accessories: unchanged
            • Background: unchanged
            • Lighting direction and color: unchanged
            • Camera angle and framing: unchanged
            • Image resolution and quality: unchanged
            
            ═══════════════════════════════════════════════════
            ONLY MODIFICATION ALLOWED - THE HAIR:
            ═══════════════════════════════════════════════════
            
            Add mega hair extensions with these EXACT specifications:
            • Length: {comprimentoIngles}
            • Color: {corIngles} (natural, realistic pigmentation)
            • Texture: {tipoIngles}
            • Application method: {metodoMegaHair} (invisible attachment)
            
            HAIR QUALITY REQUIREMENTS:
            • Photorealistic strands with natural light reflection
            • Seamless blend at the roots (no visible line or transition)
            • Natural hair fall following gravity and head shape
            • Individual strand definition (not painted look)
            • Realistic volume and movement
            • Salon-quality professional finish
            • Same lighting as the original photo
            
            ═══════════════════════════════════════════════════
            NEGATIVE PROMPT (must NOT appear):
            ═══════════════════════════════════════════════════
            • Different face, changed facial features
            • Modified skin, different age, different gender
            • Removed or added beard/mustache
            • Cartoon, painting, illustration, 3D render
            • Blurry face, distorted features
            • Different person, face swap
            • Modified background or clothing
            • Filters or beauty enhancements on the face
            
            OUTPUT: A photograph of the EXACT SAME PERSON with only the hair changed.
            The face MUST be indistinguishable from the input photo.
            """;
    }

    private static string TraduzirCor(string cor) => cor?.ToLowerInvariant() switch
    {
        "preto" => "natural jet black",
        "castanho" => "natural medium brown",
        "chocolate" => "dark chocolate brown",
        "loiro" => "natural blonde",
        "mel" => "honey blonde with warm highlights",
        _ => cor ?? "natural brown"
    };

    private static string TraduzirTipo(string tipo) => tipo?.ToLowerInvariant() switch
    {
        "liso" => "straight, sleek and smooth",
        "ondulado" => "wavy with natural loose waves",
        "cacheado" => "curly with defined natural curls",
        _ => tipo ?? "straight"
    };

    private static string TraduzirComprimento(string comprimento) => comprimento?.ToLowerInvariant() switch
    {
        "45 cm" => "shoulder length (45cm)",
        "55 cm" => "mid-back length (55cm)",
        "65 cm" => "waist length (65cm)",
        "75 cm" => "hip length (75cm)",
        "85 cm" => "thigh length (85cm), very long",
        _ => comprimento ?? "long"
    };
}