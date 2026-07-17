using SimuladorMegaHair.Domain.Interfaces;

namespace SimuladorMegaHair.Infrastructure.Services;

public class OrcamentoService : IOrcamentoService
{
    public decimal Calcular(string comprimento, string metodo)
    {
        decimal baseMetodo = metodo.ToLower() switch
        {
            "fita adesiva" => 1200m,
            "queratina" => 1500m,
            "micro link" => 1800m,
            "costurado" => 2000m,
            _ => 1000m
        };

        decimal adicionalComprimento = comprimento.ToLower() switch
        {
            "curto" => 0m,
            "médio" => 300m,
            "longo" => 600m,
            "extra longo" => 1000m,
            _ => 0m
        };

        return baseMetodo + adicionalComprimento;
    }
}