// SimuladorMegaHair.Domain/Entities/Cliente.cs
namespace SimuladorMegaHair.Domain.Entities;

public class Cliente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    // ── Navegação inversa ──────────────────────────────────
    public ICollection<Simulacao> Simulacoes { get; set; } = new List<Simulacao>();
}