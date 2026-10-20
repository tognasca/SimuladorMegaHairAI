using Microsoft.Extensions.Options;
using SimuladorMegaHair.Infrastructure.Configuration;

namespace SimuladorMegaHair.Api.Seguranca;

/// <summary>
/// Limita quantas gerações de imagem (chamadas pagas à IA) rodam ao mesmo
/// tempo. Acima do limite a API responde 429 na hora, em vez de acumular
/// requisições longas e estourar memória/limites da Replicate.
/// </summary>
public sealed class GeracaoThrottle
{
    private readonly SemaphoreSlim _semaforo;

    public GeracaoThrottle(IOptions<SimulacaoOptions> opcoes)
    {
        _semaforo = new SemaphoreSlim(Math.Max(1, opcoes.Value.MaxGeracoesSimultaneas));
    }

    /// <summary>Retorna null se não houver vaga. Dispose libera a vaga.</summary>
    public IDisposable? TentarEntrar() =>
        _semaforo.Wait(0) ? new Vaga(_semaforo) : null;

    private sealed class Vaga : IDisposable
    {
        private SemaphoreSlim? _semaforo;
        public Vaga(SemaphoreSlim semaforo) => _semaforo = semaforo;

        public void Dispose() => Interlocked.Exchange(ref _semaforo, null)?.Release();
    }
}
