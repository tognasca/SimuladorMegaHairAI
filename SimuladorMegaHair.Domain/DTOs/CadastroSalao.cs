using System.ComponentModel.DataAnnotations;

namespace SimuladorMegaHair.Domain.DTOs;

public sealed class CadastroSalao : IValidatableObject
{
    public Guid Id { get; set; }
    public long Versao { get; set; }
    [Required(ErrorMessage = "Informe o nome do salão."), StringLength(120)]
    public string Nome { get; set; } = "";
    [Required, RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Use letras minúsculas, números e hífens.")]
    [StringLength(50, MinimumLength = 2)]
    public string Identificador { get; set; } = "";
    [StringLength(120)]
    public string Responsavel { get; set; } = "";
    [EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(254)]
    public string? Email { get; set; }
    [RegularExpression("^[0-9]{10,15}$", ErrorMessage = "Informe o WhatsApp com DDI e DDD, apenas números.")]
    public string? WhatsApp { get; set; }
    [StringLength(300)]
    public string? UrlPublica { get; set; }
    public bool MegaHairPro { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!string.IsNullOrEmpty(UrlPublica) && !LinkValido(UrlPublica))
            yield return new ValidationResult("Informe um link HTTPS sem usuário, senha, consulta ou fragmento.",
                new[] { nameof(UrlPublica) });
    }

    public static bool LinkValido(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(url.Host)
        && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Query)
        && string.IsNullOrEmpty(url.Fragment) && url.AbsolutePath == "/";

    public void Normalizar()
    {
        Nome = Nome?.Trim() ?? "";
        Identificador = Identificador?.Trim() ?? "";
        Responsavel = Responsavel?.Trim() ?? "";
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim().ToLowerInvariant();
        WhatsApp = string.IsNullOrWhiteSpace(WhatsApp) ? null : WhatsApp.Trim();
        UrlPublica = string.IsNullOrWhiteSpace(UrlPublica) ? null : UrlPublica.Trim().TrimEnd('/');
        if (LinkValido(UrlPublica))
            UrlPublica = new Uri(UrlPublica!).GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
