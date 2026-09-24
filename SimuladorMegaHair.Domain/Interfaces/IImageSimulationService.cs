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

    // FASE 1 (P05): este método monta uma requisição ao modelo Kontext e
    // NÃO É MAIS CHAMADO pelo SimulacoesController — antes ele era chamado
    // logo antes de GerarSimulacaoAsync, e o resultado era descartado (a
    // IA era cobrada duas vezes por simulação, e o corpo enviado usava o
    // schema errado para o modelo Fill configurado). Mantido na interface
    // só para não quebrar a implementação existente; não religue esta
    // chamada sem revisar o schema real do modelo e o propósito dela.
    Task<(string url, string? aviso)> PipelineKontextAsync(
        string imagemPath, SimulacaoRequest req, CancellationToken ct);
}