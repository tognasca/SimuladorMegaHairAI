using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Models;
using SimuladorMegaHair.App.Services;
using SimuladorMegaHair.Domain.Enums;
using SimuladorMegaHair.Domain.Models;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(FotoPath), "FotoPath")]
[QueryProperty(nameof(ClienteId), "ClienteId")]
[QueryProperty(nameof(ClienteNome), "ClienteNome")]
public partial class StyleSelectionViewModel : BaseViewModel
{
    private readonly ApiService _apiService;
    private string? _fotoServidorPath;
    private double _lastWidth;
    private double _lastHeight;

    /// <summary>
    /// Cliente vinculado a esta simulação (opcional). Vem preenchido quando
    /// o fluxo começou em "Buscar Cliente" → "Nova simulação para este
    /// cliente". Trafega como string na navegação (Shell não converte Guid?
    /// automaticamente) e é convertido na hora de montar o request.
    /// </summary>
    [ObservableProperty]
    private string? clienteId;

    [ObservableProperty]
    private string? clienteNome;

    public bool TemClienteVinculado => !string.IsNullOrWhiteSpace(ClienteNome);

    partial void OnClienteNomeChanged(string? value)
        => OnPropertyChanged(nameof(TemClienteVinculado));

    // ═════════════════════════════════════════════════════════
    // PROPRIEDADES OBSERVÁVEIS - BÁSICAS
    // ═════════════════════════════════════════════════════════

    [ObservableProperty]
    private string fotoOriginal = string.Empty;

    [ObservableProperty]
    private string fotoPath = string.Empty;

    /// <summary>
    /// Foto atualmente exibida (pode ser a original ou selecionada do histórico).
    /// </summary>
    [ObservableProperty]
    private string fotoExibicao = AppSettings.StaticResultUrl;

    /// <summary>
    /// URL da imagem gerada pela IA (usada como "DEPOIS").
    /// </summary>
    [ObservableProperty]
    private string? fotoResultadoUrl;

    [ObservableProperty]
    private bool temResultado;

    [ObservableProperty]
    private string comprimentoSelecionado = string.Empty;

    [ObservableProperty]
    private string corSelecionada = string.Empty;

    [ObservableProperty]
    private string tipoCabeloSelecionado = string.Empty;

    [ObservableProperty]
    private string metodoSelecionado = string.Empty;

    [ObservableProperty]
    private string volumeSelecionado = string.Empty;

    [ObservableProperty]
    private decimal valorAtual;

    [ObservableProperty]
    private bool isPortrait = true;

    [ObservableProperty]
    private bool isLandscape;

    [ObservableProperty]
    private string layoutModeSelected = Preferences.Get("LayoutMode", "Automático");

    [ObservableProperty]
    private bool mostrandoDepois = true;

    // Modo de visualização: "Slider", "Antes", "Depois"
    [ObservableProperty]
    private string modoVisualizacao = "Slider";

    // ═════════════════════════════════════════════════════════
    // PROPRIEDADES DE VOLUME
    // ═════════════════════════════════════════════════════════

    private int _volumeNivel = 2; // 1=100g, 2=200g, 3=300g, 4=400g

    /// <summary>
    /// Nível selecionado (1-4). Controla qual botão fica ativo no UI.
    /// </summary>
    [ObservableProperty]
    private int volumeNivel = 2;

    /// <summary>
    /// Quantidade em gramas baseada no nível.
    /// </summary>
    public int GramasAtuais => VolumeNivel * 100;

    /// <summary>
    /// Caminho da imagem processada com volume.
    /// </summary>
    private string? _fotoVolumeProcessada;

    /// <summary>
    /// URL final para exibir: preferencialmente a com volume, senão a original da IA.
    /// </summary>
    public string FotoVolumeAtiva =>
        !string.IsNullOrWhiteSpace(_fotoVolumeProcessada)
            ? $"{AppSettings.ApiBaseUrl}/api/simulacoes/{_fotoVolumeProcessada.TrimStart('/')}"
            : FotoResultadoUrl ?? string.Empty;

    [ObservableProperty]
    private bool ajustandoVolume;

    /// <summary>
    /// Indica se já houve algum ajuste de volume aplicado.
    /// </summary>
    public bool TemVolumeAjustado => !string.IsNullOrWhiteSpace(_fotoVolumeProcessada);

    /// <summary>
    /// Texto descritivo do nível de volume selecionado.
    /// </summary>
    [ObservableProperty]
    private string volumeTexto = "💫 Perfeito para uso diário (200g)";

    public ObservableCollection<VolumeItem> NiveisVolume { get; } = new()
    {
        new VolumeItem(1, "✨ Look natural e leve", "100g"),
        new VolumeItem(2, "💫 Perfeito para uso diário", "200g"),
        new VolumeItem(3, "🔥 Cabelo cheio e definido", "300g"),
        new VolumeItem(4, "⭐ Maximum volume", "400g")
    };

    public record VolumeItem(int Nivel, string Descricao, string Gramas);

    // ═════════════════════════════════════════════════════════
    // PROPRIEDADES DERIVADAS (Modos de Visualização)
    // ═════════════════════════════════════════════════════════

    public bool ModoSlider => ModoVisualizacao == "Slider";
    public bool ModoAntes => ModoVisualizacao == "Antes";
    public bool ModoDepois => ModoVisualizacao == "Depois";
    public bool TemHistorico => Historico.Count > 0;

    partial void OnModoVisualizacaoChanged(string value)
    {
        OnPropertyChanged(nameof(ModoSlider));
        OnPropertyChanged(nameof(ModoAntes));
        OnPropertyChanged(nameof(ModoDepois));
    }

    partial void OnVolumeNivelChanged(int value)
    {
        VolumeTexto = NiveisVolume.First(v => v.Nivel == value).Descricao + $" ({value * 100}g)";
        OnPropertyChanged(nameof(GramasAtuais));
        OnPropertyChanged(nameof(FotoVolumeAtiva));
        OnPropertyChanged(nameof(TemVolumeAjustado));
    }

    // ═════════════════════════════════════════════════════════
    // COLEÇÕES E LISTAS
    // ═════════════════════════════════════════════════════════

    public ObservableCollection<string> Comprimentos { get; } = new(OpcoesVisual.Comprimentos);
    public ObservableCollection<string> Cores { get; } = new(OpcoesVisual.Cores);
    public ObservableCollection<string> TiposCabelo { get; } = new(OpcoesVisual.TiposCabelo);
    public ObservableCollection<string> Metodos { get; } = new(OpcoesVisual.Metodos);

    // Legacy - mantido por compatibilidade
    public ObservableCollection<string> Volumes { get; } = new() { "100 g", "150 g", "200 g", "250 g" };

    public ObservableCollection<SimulacaoResponse> Historico { get; } = new();

    public IReadOnlyList<string> LayoutModes { get; } = new List<string>
    {
        "Automático",
        "Vertical",
        "Horizontal"
    };

    public List<string> ComprimentosLista { get; } = new()
    {
        "45 cm", "55 cm", "65 cm", "75 cm", "85 cm"
    };

    public List<string> CoresLista { get; } = new()
    {
        "Preto", "Castanho", "Chocolate", "Loiro", "Mel"
    };

    public List<string> TiposLista { get; } = new()
    {
        "Liso", "Ondulado", "Cacheado"
    };

    public List<string> MetodosLista { get; } = new()
    {
        "Fita", "Micro Cápsula", "Queratina"
    };

    // ID da simulação atual para chamar o endpoint de volume
    // (era um "int" derivado de GetHashCode() do Guid — nunca batia com o
    // registro real no banco, então o ajuste de volume sempre falhava
    // silenciosamente. Corrigido para usar o Guid de verdade.)
    private Guid? _simulacaoAtualId;

    // ═════════════════════════════════════════════════════════
    // CONSTRUTOR
    // ═════════════════════════════════════════════════════════

    public StyleSelectionViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "SEU NOVO VISUAL";

        Historico.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TemHistorico));
    }

    // ═════════════════════════════════════════════════════════
    // COMANDOS
    // ═════════════════════════════════════════════════════════

    [RelayCommand]
    private void SelectComprimento(string valor) => ComprimentoSelecionado = valor;

    [RelayCommand]
    private void SelectCor(string valor) => CorSelecionada = valor;

    [RelayCommand]
    private void SelectTipo(string valor) => TipoCabeloSelecionado = valor;

    [RelayCommand]
    private void SelectMetodo(string valor) => MetodoSelecionado = valor;

    [RelayCommand]
    private void SelectVolume(string valor) => VolumeSelecionado = valor;

    [RelayCommand]
    private void AlternarAntesDepois() => MostrandoDepois = !MostrandoDepois;

    [RelayCommand]
    private void MudarModo(string modo) => ModoVisualizacao = modo;

    /// <summary>
    /// Comando para ajustar volume
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task AjustarVolume(int nivel)
    {
        if (!TemResultado || _simulacaoAtualId is null || _simulacaoAtualId == Guid.Empty)
        {
            await Shell.Current.DisplayAlert("Info",
                "Gere uma simulação primeiro para ajustar o volume.", "OK");
            return;
        }

        if (VolumeNivel == nivel && TemVolumeAjustado)
            return;

        try
        {
            AjustandoVolume = true;

            var request = new AjustarVolumeRequest(nivel)
            {
                ImagemOriginalPath = FotoOriginal,
                ImagemResultadoPath = FotoResultadoUrl
            };

            var resposta = await _apiService.AjustarVolumeAsync(_simulacaoAtualId.Value, request);

            if (resposta != null && !string.IsNullOrWhiteSpace(resposta.FotoResultadoUrl))
            {
                _fotoVolumeProcessada = resposta.FotoResultadoUrl;
                VolumeNivel = nivel;

                // Força refresh da imagem
                if (ModoDepois)
                {
                    MudarModo("Antes");
                    await Task.Delay(30);
                    MudarModo("Depois");
                }
            }
            else
            {
                await Shell.Current.DisplayAlert("Aviso",
                    "Não foi possível aplicar esse volume. Tente novamente.", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Falha ao ajustar volume: {ex.Message}", "OK");
        }
        finally
        {
            AjustandoVolume = false;
        }
    }

    // ═════════════════════════════════════════════════════════
    // CALLBACKS DE PROPRIEDADES
    // ═════════════════════════════════════════════════════════

    partial void OnFotoPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            FotoExibicao = value;
            FotoOriginal = value;
        }

        if (!AppSettings.UsarImagemDeTeste)
            _ = InicializarAsync();
    }

    partial void OnLayoutModeSelectedChanged(string value)
    {
        Preferences.Set("LayoutMode", value);
        AtualizarLayout(_lastWidth, _lastHeight);
    }

    // ═════════════════════════════════════════════════════════
    // MÉTODOS PÚBLICOS
    // ═════════════════════════════════════════════════════════

    public void AtualizarLayout(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        _lastWidth = width;
        _lastHeight = height;

        bool usarVertical = LayoutModeSelected switch
        {
            "Vertical" => true,
            "Horizontal" => false,
            _ => height > width
        };

        IsPortrait = usarVertical;
        IsLandscape = !usarVertical;
    }

    public void AtualizarImagemDeTeste()
    {
        FotoExibicao = AppSettings.StaticResultUrl;
    }

    private void ResetarVolume()
    {
        _volumeNivel = 2;
        VolumeNivel = 2;
        _fotoVolumeProcessada = null;
        VolumeTexto = "💫 Perfeito para uso diário (200g)";
    }

    // ═════════════════════════════════════════════════════════
    // MÉTODOS PRIVADOS
    // ═════════════════════════════════════════════════════════

    private async Task InicializarAsync()
    {
        if (string.IsNullOrWhiteSpace(FotoPath)) return;

        try
        {
            IsBusy = true;
            _fotoServidorPath = await _apiService.UploadFotoAsync(FotoPath);
            await CarregarHistoricoAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CarregarHistoricoAsync()
    {
        if (AppSettings.UsarImagemDeTeste) return;

        try
        {
            Historico.Clear();
            var itens = await _apiService.GetHistoricoAsync(_fotoServidorPath);
            foreach (var item in itens)
                Historico.Add(item);
        }
        catch
        {
            // Silencioso
        }
    }

    [RelayCommand]
    private async Task GerarSimulacaoAsync()
    {
        if (string.IsNullOrWhiteSpace(FotoOriginal))
        {
            await Shell.Current.DisplayAlert(
                "Atenção",
                "Nenhuma foto foi selecionada. Volte e escolha uma foto primeiro.",
                "OK");
            return;
        }

        IsBusy = true;
        await Task.Yield();
        await Task.Delay(50);

        try
        {
            SimulacaoResponse resultado;

            if (AppSettings.UsarImagemDeTeste)
            {
                var existente = Historico.FirstOrDefault(x =>
                    x.Comprimento == ComprimentoSelecionado &&
                    x.Cor == CorSelecionada &&
                    x.TipoCabelo == TipoCabeloSelecionado &&
                    x.MetodoMegaHair == MetodoSelecionado);

                resultado = existente ?? new SimulacaoResponse
                {
                    Id = Guid.NewGuid(),
                    FotoOriginalUrl = FotoOriginal,
                    FotoResultadoUrl = AppSettings.StaticResultUrl,
                    Comprimento = ComprimentoSelecionado,
                    Cor = CorSelecionada,
                    TipoCabelo = TipoCabeloSelecionado,
                    MetodoMegaHair = MetodoSelecionado,
                    ValorEstimado = CalcularValorTeste(),
                    CriadoEm = DateTime.Now
                };

                await Task.Delay(3000);

                if (existente is null)
                    Historico.Insert(0, resultado);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_fotoServidorPath))
                {
                    await Shell.Current.DisplayAlert("Atenção", "A foto ainda não foi enviada.", "OK");
                    return;
                }

                var request = new CriarSimulacaoRequest
                {
                    FotoOriginalPath = _fotoServidorPath,
                    Comprimento = ComprimentoSelecionado,
                    Cor = CorSelecionada,
                    TipoCabelo = TipoCabeloSelecionado,
                    MetodoMegaHair = MetodoSelecionado,
                    Provider = ImageProvider.Replicate,
                    ClienteId = Guid.TryParse(ClienteId, out var cid) ? cid : null
                };

                resultado = await _apiService.CriarSimulacaoAsync(request)
                    ?? throw new InvalidOperationException("A API não retornou a simulação.");

                resultado.FotoOriginalUrl = FotoOriginal;

                await CarregarHistoricoAsync();
            }

            // PREENCHE RESULTADOS
            FotoExibicao = FotoOriginal;
            FotoResultadoUrl = resultado.FotoResultadoUrl;
            ValorAtual = resultado.ValorEstimado ?? 0;
            TemResultado = true;
            _simulacaoAtualId = resultado.Id;

            ComprimentoSelecionado = resultado.Comprimento;
            CorSelecionada = resultado.Cor;
            TipoCabeloSelecionado = resultado.TipoCabelo;
            MetodoSelecionado = resultado.MetodoMegaHair;

            ResetarVolume();

            ModoVisualizacao = "Slider";
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelecionarDoHistorico(SimulacaoResponse item)
    {
        if (item is null) return;

        FotoExibicao = FotoOriginal;
        FotoResultadoUrl = item.FotoResultadoUrl;
        ValorAtual = item.ValorEstimado ?? 0;

        ComprimentoSelecionado = item.Comprimento;
        CorSelecionada = item.Cor;
        TipoCabeloSelecionado = item.TipoCabelo;
        MetodoSelecionado = item.MetodoMegaHair;
        TemResultado = true;
        _simulacaoAtualId = item.Id;

        ResetarVolume();

        ModoVisualizacao = "Slider";
    }

    [RelayCommand]
    private async Task VerOrcamentoAsync()
    {
        var parametros = new Dictionary<string, object>
        {
            ["ValorEstimado"] = ValorAtual,
            ["Metodo"] = MetodoSelecionado,
            ["Comprimento"] = ComprimentoSelecionado,
            ["Gramas"] = GramasAtuais
        };

        await Shell.Current.GoToAsync("//BudgetPage", parametros);
    }

    private decimal CalcularValorTeste()
    {
        return MetodoSelecionado.ToLowerInvariant() switch
        {
            "fita" => 1800m,
            "micro cápsula" => 2200m,
            "queratina" => 2500m,
            _ => 1800m
        };
    }
}