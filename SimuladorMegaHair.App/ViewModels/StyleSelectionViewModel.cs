using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SimuladorMegaHair.App.Models;
using SimuladorMegaHair.App.Services;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(FotoPath), "FotoPath")]
public partial class StyleSelectionViewModel : BaseViewModel
{
    private readonly ApiService _apiService;
    private string? _fotoServidorPath;
    private double _lastWidth;
    private double _lastHeight;

    // ═════════════════════════════════════════════════════════
    // PROPRIEDADES OBSERVÁVEIS
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Foto original do usuário (usada como "ANTES" no comparador).
    /// Nunca é sobrescrita pelo resultado da IA.
    /// </summary>
    [ObservableProperty]
    private string fotoOriginal = string.Empty;

    [ObservableProperty]
    private string fotoPath = string.Empty;

    /// <summary>
    /// Foto atualmente exibida (pode ser a original ou uma selecionada do histórico).
    /// </summary>
    [ObservableProperty]
    private string fotoExibicao = AppSettings.StaticResultUrl;

    /// <summary>
    /// URL da imagem gerada pela IA (usada como "DEPOIS" no comparador).
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
    private string volumeSelecionado = "150 g";

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
    // PROPRIEDADES DERIVADAS
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

    // ═════════════════════════════════════════════════════════
    // COLEÇÕES
    // ═════════════════════════════════════════════════════════

    public ObservableCollection<string> Comprimentos { get; } = new(OpcoesVisual.Comprimentos);
    public ObservableCollection<string> Cores { get; } = new(OpcoesVisual.Cores);
    public ObservableCollection<string> TiposCabelo { get; } = new(OpcoesVisual.TiposCabelo);
    public ObservableCollection<string> Metodos { get; } = new(OpcoesVisual.Metodos);
    public ObservableCollection<SimulacaoResponse> Historico { get; } = new();
    public ObservableCollection<string> Volumes { get; } = new() { "100 g", "150 g", "200 g", "250 g" };

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

    // ═════════════════════════════════════════════════════════
    // CONSTRUTOR
    // ═════════════════════════════════════════════════════════

    public StyleSelectionViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "SEU NOVO VISUAL";
        //FotoExibicao = AppSettings.StaticResultUrl;

        Historico.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TemHistorico));
    }

    // ═════════════════════════════════════════════════════════
    // COMANDOS DE SELEÇÃO
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

    // ═════════════════════════════════════════════════════════
    // CALLBACKS DE PROPRIEDADES
    // ═════════════════════════════════════════════════════════

    partial void OnFotoPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            FotoExibicao = value;   // ← Mostra a foto real do usuário
            FotoOriginal = value;   // ← Guarda como "ANTES"
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

    // ═════════════════════════════════════════════════════════
    // COMANDOS DE AÇÃO
    // ═════════════════════════════════════════════════════════


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

        try
        {
            IsBusy = true;

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
                    FotoOriginalUrl = FotoOriginal,                    // ✅ FOTO LOCAL DO USUÁRIO
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
                    MetodoMegaHair = MetodoSelecionado
                };

                resultado = await _apiService.CriarSimulacaoAsync(request)
                    ?? throw new InvalidOperationException("A API não retornou a simulação.");

                // 🔒 FORÇA a foto original a ser SEMPRE a do usuário
                // (ignora o que a API retornar)
                resultado.FotoOriginalUrl = FotoOriginal;

                await CarregarHistoricoAsync();
            }

            // ✅ ANTES = foto do usuário  |  DEPOIS = resultado da IA
            FotoExibicao = FotoOriginal;                     // ← Sempre a foto local
            FotoResultadoUrl = resultado.FotoResultadoUrl;   // ← Sempre o resultado da IA
            ValorAtual = resultado.ValorEstimado ?? 0;
            TemResultado = true;

            ComprimentoSelecionado = resultado.Comprimento;
            CorSelecionada = resultado.Cor;
            TipoCabeloSelecionado = resultado.TipoCabelo;
            MetodoSelecionado = resultado.MetodoMegaHair;

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

    //[RelayCommand]
    //private async Task GerarSimulacaoAsync()
    //{
    //    // ⚠️ Validação: precisa ter uma foto original
    //    if (string.IsNullOrWhiteSpace(FotoOriginal))
    //    {
    //        await Shell.Current.DisplayAlert(
    //            "Atenção",
    //            "Nenhuma foto foi selecionada. Volte e tire/escolha uma foto primeiro.",
    //            "OK");
    //        return;
    //    }

    //    try
    //    {
    //        IsBusy = true;

    //        SimulacaoResponse resultado;

    //        if (AppSettings.UsarImagemDeTeste)
    //        {
    //            var existente = Historico.FirstOrDefault(x =>
    //                x.Comprimento == ComprimentoSelecionado &&
    //                x.Cor == CorSelecionada &&
    //                x.TipoCabelo == TipoCabeloSelecionado &&
    //                x.MetodoMegaHair == MetodoSelecionado);

    //            resultado = existente ?? new SimulacaoResponse
    //            {
    //                Id = Guid.NewGuid(),
    //                FotoOriginalUrl = FotoOriginal,                    // ← FOTO REAL DO USUÁRIO
    //                FotoResultadoUrl = AppSettings.StaticResultUrl,    // ← Resultado simulado da IA
    //                Comprimento = ComprimentoSelecionado,
    //                Cor = CorSelecionada,
    //                TipoCabelo = TipoCabeloSelecionado,
    //                MetodoMegaHair = MetodoSelecionado,
    //                ValorEstimado = CalcularValorTeste(),
    //                CriadoEm = DateTime.Now
    //            };

    //            await Task.Delay(3000); // Simula tempo de processamento da IA

    //            if (existente is null)
    //                Historico.Insert(0, resultado);
    //        }
    //        else
    //        {
    //            if (string.IsNullOrWhiteSpace(_fotoServidorPath))
    //            {
    //                await Shell.Current.DisplayAlert("Atenção", "A foto ainda não foi enviada.", "OK");
    //                return;
    //            }

    //            var request = new CriarSimulacaoRequest
    //            {
    //                FotoOriginalPath = _fotoServidorPath,
    //                Comprimento = ComprimentoSelecionado,
    //                Cor = CorSelecionada,
    //                TipoCabelo = TipoCabeloSelecionado,
    //                MetodoMegaHair = MetodoSelecionado
    //            };

    //            resultado = await _apiService.CriarSimulacaoAsync(request)
    //                ?? throw new InvalidOperationException("A API não retornou a simulação.");

    //            // Se a API não retornar a URL original, usa a que já temos localmente
    //            if (string.IsNullOrWhiteSpace(resultado.FotoOriginalUrl))
    //                resultado.FotoOriginalUrl = FotoOriginal;

    //            await CarregarHistoricoAsync();
    //        }

    //        // ✅ FotoExibicao = foto do usuário (ANTES)
    //        // ✅ FotoResultadoUrl = resultado da IA (DEPOIS)
    //        FotoExibicao = resultado.FotoOriginalUrl;
    //        FotoResultadoUrl = resultado.FotoResultadoUrl;
    //        ValorAtual = resultado.ValorEstimado ?? 0;
    //        TemResultado = true;

    //        ComprimentoSelecionado = resultado.Comprimento;
    //        CorSelecionada = resultado.Cor;
    //        TipoCabeloSelecionado = resultado.TipoCabelo;
    //        MetodoSelecionado = resultado.MetodoMegaHair;

    //        ModoVisualizacao = "Slider";
    //    }
    //    catch (Exception ex)
    //    {
    //        await Shell.Current.DisplayAlert("Erro", ex.Message, "OK");
    //    }
    //    finally
    //    {
    //        IsBusy = false;
    //    }
    //}
    [RelayCommand]
    private void SelecionarDoHistorico(SimulacaoResponse item)
    {
        if (item is null) return;

        // ✅ Sempre usa a foto local como ANTES
        FotoExibicao = FotoOriginal;
        FotoResultadoUrl = item.FotoResultadoUrl;
        ValorAtual = item.ValorEstimado ?? 0;

        ComprimentoSelecionado = item.Comprimento;
        CorSelecionada = item.Cor;
        TipoCabeloSelecionado = item.TipoCabelo;
        MetodoSelecionado = item.MetodoMegaHair;
        TemResultado = true;

        ModoVisualizacao = "Slider";
    }

    [RelayCommand]
    private async Task VerOrcamentoAsync()
    {
        var parametros = new Dictionary<string, object>
        {
            ["ValorEstimado"] = ValorAtual,
            ["Metodo"] = MetodoSelecionado,
            ["Comprimento"] = ComprimentoSelecionado
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