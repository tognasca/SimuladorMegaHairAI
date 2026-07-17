namespace SimuladorMegaHair.Domain.Interfaces;

public interface IImageSimulationService
{
    Task<string> GerarSimulacaoAsync(
        string imagemOriginalPath,
        string comprimento,
        string cor,
        string tipoCabelo,
        string metodoMegaHair,
        CancellationToken cancellationToken = default);
}