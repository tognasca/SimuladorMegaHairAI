namespace SimuladorMegaHair.Domain.Entities;

public class CatalogoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titulo { get; set; } = string.Empty;
    public string FotoPath { get; set; } = string.Empty;

    public string Comprimento { get; set; } = string.Empty;
    public string Cor { get; set; } = string.Empty;
    public string TipoCabelo { get; set; } = string.Empty;
    public string MetodoMegaHair { get; set; } = string.Empty;

    public decimal PrecoBase { get; set; }
    public bool AutorizadoUsoImagem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}