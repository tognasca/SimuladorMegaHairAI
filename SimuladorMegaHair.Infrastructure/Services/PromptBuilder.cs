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
            Edit this photo realistically.

            Keep exactly:
            - The person's face, facial identity and expression
            - The original lighting and framing
            - Makeup, skin tone and background

            Apply a mega hair extension simulation with these characteristics:
            - Length: {comprimento}
            - Color: {cor}
            - Hair type: {tipoCabelo}
            - Application method: {metodoMegaHair}

            The result must look natural and professional, as if done in a real beauty salon.
            Focus only on the hair. Do not change anything else.
            """;
    }
}