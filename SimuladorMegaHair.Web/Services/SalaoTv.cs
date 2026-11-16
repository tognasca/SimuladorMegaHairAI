using System.Security.Cryptography;

namespace SimuladorMegaHair.Web.Services;

public sealed class SalaoTv
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Sessao> _sessoes = new();

    public record Pareamento(string Codigo, string Chave);
    public record Estado(string Revisao, Apresentacao Tela);
    public record Apresentacao(string Fase, string? Antes = null, string? Depois = null,
        string? Estilo = null, string? Orcamento = null);
    private sealed class Sessao(string codigo)
    {
        public string Codigo { get; } = codigo;
        public DateTimeOffset Expira { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);
        public string? Controle { get; set; }
        public Estado Estado { get; set; } = new(Guid.NewGuid().ToString("N"), new("aguardando"));
    }

    public Pareamento? Criar()
    {
        lock (_gate)
        {
            LimparExpiradas();
            if (_sessoes.Count >= 64) return null;
            string codigo;
            do { codigo = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); }
            while (_sessoes.Values.Any(s => s.Codigo == codigo));
            var chave = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessoes.Add(chave, new Sessao(codigo));
            return new(codigo, chave);
        }
    }

    public string? Parear(string codigo)
    {
        lock (_gate)
        {
            LimparExpiradas();
            var sessao = _sessoes.Values.FirstOrDefault(s => s.Codigo == codigo && s.Controle is null);
            if (sessao is null) return null;
            sessao.Controle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessao.Expira = DateTimeOffset.UtcNow.AddHours(1);
            sessao.Estado = new(Guid.NewGuid().ToString("N"), new("pronto"));
            return sessao.Controle;
        }
    }

    public Estado? Ler(string chave)
    {
        lock (_gate)
        {
            LimparExpiradas();
            return _sessoes.TryGetValue(chave, out var sessao) ? sessao.Estado : null;
        }
    }

    public bool Mostrar(string controle, Apresentacao tela)
    {
        lock (_gate)
        {
            LimparExpiradas();
            var sessao = _sessoes.Values.FirstOrDefault(s => s.Controle == controle);
            if (sessao is null) return false;
            if (sessao.Estado.Tela != tela)
                sessao.Estado = new(Guid.NewGuid().ToString("N"), tela);
            return true;
        }
    }

    public void Encerrar(string controle)
    {
        lock (_gate)
        {
            var chave = _sessoes.FirstOrDefault(p => p.Value.Controle == controle).Key;
            if (chave is not null) _sessoes.Remove(chave);
        }
    }

    private void LimparExpiradas()
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var chave in _sessoes.Where(p => p.Value.Expira <= agora).Select(p => p.Key).ToArray())
            _sessoes.Remove(chave);
    }
}
