
namespace SimuladorMegaHair.Domain.Interfaces;
using SimuladorMegaHair.Domain.Models;
public interface IImageSimulationService
{
    Task<SimulacaoResult> GerarSimulacaoAsync(
        SimulacaoRequest request,
        CancellationToken ct = default);
}