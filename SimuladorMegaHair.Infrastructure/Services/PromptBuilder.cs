namespace SimuladorMegaHair.Infrastructure.Services;

public static class PromptBuilder
{
    public static string Build(
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair)
    {
        return $"""
            Professional beauty salon hair simulation.

            CRITICAL RULES - DO NOT VIOLATE:
            - DO NOT change the person's face in any way
            - DO NOT modify facial features, identity, or expression
            - DO NOT change skin tone, makeup, or eyes
            - DO NOT alter the background or lighting
            - DO NOT change body, clothes or accessories
            - ONLY modify the hair area

            Apply mega hair extensions with these exact characteristics:
            - Length: {comprimento}
            - Color: {cor} (realistic and vibrant)
            - Hair type/texture: {tipoCabelo}
            - Application method: {metodoMegaHair}

            The result must be:
            - Photorealistic and high resolution
            - Natural blend at the roots (no visible transitions)
            - Professional salon quality
            - Same facial identity as the original photo
            
            Focus 100% on the hair. Everything else stays identical.
            """;
    }
}