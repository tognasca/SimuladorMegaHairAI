namespace SimuladorMegaHair.Domain.Entities;

/// <summary>
/// Conta de acesso à API (ex.: uma por salão/operador). Autenticação
/// simples por usuário/senha + JWT — ver AuthController e Program.cs.
/// </summary>
public class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash BCrypt da senha. Nunca armazenar senha em texto puro.</summary>
    public string SenhaHash { get; set; } = string.Empty;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
