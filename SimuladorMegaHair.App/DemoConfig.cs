namespace SimuladorMegaHair.App;

public static class DemoConfig
{
    // Deixe true enquanto estiver ajustando o layout.
    // Depois altere para false para voltar a usar a IA.
    public const bool UseStaticImage = true;

    public const string StaticResultFileName =
        "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png";

    // A API precisa estar rodando com:
    // dotnet run --launch-profile http
    public const string ApiBaseUrl = "http://localhost:5185/";

    public static string StaticResultUrl =>
        $"{ApiBaseUrl}resultados/{StaticResultFileName}";
}