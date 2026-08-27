namespace SimuladorMegaHair.Domain.Interfaces;

using SimuladorMegaHair.Domain.DTOs;
using SimuladorMegaHair.Domain.Models;

public interface IImageSimulationService
{
    Task<SimulacaoResult> GerarSimulacaoAsync(
        SimulacaoRequest request,
        CancellationToken ct = default);

    Task<string> AjustarVolumeAsync(
        AjustarVolumeRequest req,
        CancellationToken ct = default);

    Task<(string url, string? aviso)> PipelineKontextAsync(
        string imagemPath, SimulacaoRequest req, CancellationToken ct);
}