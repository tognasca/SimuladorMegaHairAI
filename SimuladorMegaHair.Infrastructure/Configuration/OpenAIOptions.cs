// SimuladorMegaHair.Infrastructure/Configuration/OpenAIOptions.cs
namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class OpenAIOptions
{
    public const string Section = "OpenAI";
    public string ApiToken { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-image-1";
    public string Size { get; set; } = "1024x1024";
    public string Quality { get; set; } = "high";
}