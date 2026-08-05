// SimuladorMegaHair.Infrastructure/Configuration/SimulacaoOptions.cs
using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class SimulacaoOptions
{
    public const string Section = "Simulacao";

    /// Provider padrão se o usuário não escolher
    public ImageProvider DefaultProvider { get; set; } = ImageProvider.Local;

    /// Mostra opção gratuita no frontend
    public bool HabilitarProviderLocal { get; set; } = true;

    /// Mostra opção Replicate no frontend
    public bool HabilitarProviderReplicate { get; set; } = true;

    /// Mostra opção OpenAI no frontend
    public bool HabilitarProviderOpenAI { get; set; } = false;
}