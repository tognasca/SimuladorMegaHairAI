// SimuladorMegaHair.Infrastructure/Configuration/SimulacaoOptions.cs
using SimuladorMegaHair.Domain.Enums;

namespace SimuladorMegaHair.Infrastructure.Configuration;

public sealed class SimulacaoOptions
{
    public const string Section = "Simulacao";

    /// Provider padrão se o usuário não escolher.
    /// Só "Replicate" está implementado hoje (ver SimulacaoPipelineService).
    public ImageProvider DefaultProvider { get; set; } = ImageProvider.Replicate;

    /// NÃO IMPLEMENTADO: mantido em false para não anunciar uma opção que falha.
    public bool HabilitarProviderLocal { get; set; } = false;

    /// Mostra opção Replicate no frontend
    public bool HabilitarProviderReplicate { get; set; } = true;

    /// NÃO IMPLEMENTADO: mantido em false para não anunciar uma opção que falha.
    public bool HabilitarProviderOpenAI { get; set; } = false;

    // ── Limites de uso (protegem o crédito da IA e a memória do servidor) ──

    /// Máximo de simulações criadas nas últimas 24 h em todo o sistema.
    public int LimiteDiarioGlobal { get; set; } = 200;

    /// Máximo de gerações por hora vindas de um mesmo endereço de rede.
    /// Atenção: o site (Web) fala com a API por um único endereço, então este
    /// limite é compartilhado por todos os aparelhos que usam o site.
    public int LimitePorHoraPorOrigem { get; set; } = 60;

    /// Gerações de IA executando ao mesmo tempo (acima disso: HTTP 429).
    public int MaxGeracoesSimultaneas { get; set; } = 3;

    // ── Privacidade ──

    /// Salvar máscara e overlay de auditoria (mostram o rosto). Só para
    /// depuração; desligado por padrão. Quando ligado, são apagados em 24 h.
    public bool SalvarAuditoriaMascara { get; set; } = false;

    /// Validade das URLs assinadas das fotos.
    public int ValidadeUrlMidiaMinutos { get; set; } = 240;

    /// Idade mínima para apagar arquivos de wwwroot/temp deixados por falhas.
    public int RetencaoTempMinutos { get; set; } = 60;
}
