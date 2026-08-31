namespace SimuladorMegaHair.Domain.DTOs;

/// <summary>
/// Dados básicos de um cliente, usados em listagens (busca/autocomplete).
/// </summary>
public class ClienteResponse
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTime CriadoEm { get; set; }

    /// <summary>Total de simulações já feitas por esse cliente.</summary>
    public int TotalSimulacoes { get; set; }

    /// <summary>Data da última simulação (se houver).</summary>
    public DateTime? UltimaSimulacaoEm { get; set; }
}

/// <summary>
/// Detalhe de um cliente, incluindo todo o seu histórico de simulações
/// (para a tela "Buscar cliente que já fez simulação").
/// </summary>
public class ClienteDetalheResponse
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTime CriadoEm { get; set; }

    public List<SimulacaoResponse> Simulacoes { get; set; } = new();
}

public class CriarClienteRequest
{
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
}

public class AtualizarClienteRequest
{
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
}
