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

    [ObservableProperty]
    private string fotoPath = string.Empty;

    [ObservableProperty]
    private string fotoExibicao = AppSettings.StaticResultUrl;

    [ObservableProperty]
    private string? fotoResultadoUrl;

    [ObservableProperty]
    private bool temResultado;

    [ObservableProperty]
    private string comprimentoSelecionado = "Longo";

    [ObservableProperty]
    private string corSelecionada = "Castanho Escuro";

    [ObservableProperty]
    private string tipoCabeloSelecionado = "Liso";

    [ObservableProperty]
    private string metodoSelecionado = "Fita Adesiva";

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

    [RelayCommand]
    private void AlternarAntesDepois()
    {
        MostrandoDepois = !MostrandoDepois;
    }
    public ObservableCollection<string> Comprimentos { get; } = new(OpcoesVisual.Comprimentos);
    public ObservableCollection<string> Cores { get; } = new(OpcoesVisual.Cores);
    public ObservableCollection<string> TiposCabelo { get; } = new(OpcoesVisual.TiposCabelo);
    public ObservableCollection<string> Metodos { get; } = new(OpcoesVisual.Metodos);
    public ObservableCollection<SimulacaoResponse> Historico { get; } = [];

    public IReadOnlyList<string> LayoutModes { get; } = new List<string>
    {
        "Automático",
        "Vertical",
        "Horizontal"
    };

    public StyleSelectionViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "SEU NOVO VISUAL";
        FotoExibicao = AppSettings.StaticResultUrl;
    }

    partial void OnFotoPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            FotoExibicao = value;
        }

        if (!AppSettings.UsarImagemDeTeste)
        {
            _ = InicializarAsync();
        }
    }

    partial void OnLayoutModeSelectedChanged(string value)
    {
        Preferences.Set("LayoutMode", value);
        AtualizarLayout(_lastWidth, _lastHeight);
    }

    public void AtualizarLayout(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;

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
        if (AppSettings.UsarImagemDeTeste)
            return;

        try
        {
            Historico.Clear();
            var itens = await _apiService.GetHistoricoAsync(_fotoServidorPath);
            foreach (var item in itens)
                Historico.Add(item);
        }
        catch
        {
            // Silencioso para não interromper a experiência
        }
    }

    [RelayCommand]
    private async Task GerarSimulacaoAsync()
    {
        try
        {
            IsBusy = true;

            SimulacaoResponse resultado;

            if (AppSettings.UsarImagemDeTeste)
            {
                // Procura primeiro um visual já criado com as mesmas opções
                var existente = Historico.FirstOrDefault(x =>
                    x.Comprimento == ComprimentoSelecionado &&
                    x.Cor == CorSelecionada &&
                    x.TipoCabelo == TipoCabeloSelecionado &&
                    x.MetodoMegaHair == MetodoSelecionado);

                resultado = existente ?? new SimulacaoResponse
                {
                    Id = Guid.NewGuid(),
                    FotoOriginalUrl = FotoExibicao,
                    FotoResultadoUrl = AppSettings.StaticResultUrl,
                    Comprimento = ComprimentoSelecionado,
                    Cor = CorSelecionada,
                    TipoCabelo = TipoCabeloSelecionado,
                    MetodoMegaHair = MetodoSelecionado,
                    ValorEstimado = CalcularValorTeste(),
                    CriadoEm = DateTime.Now
                };

                // Simula uma pequena transição
                await Task.Delay(350);

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

                await CarregarHistoricoAsync();
            }

            FotoExibicao = resultado.FotoOriginalUrl;
            FotoResultadoUrl = resultado.FotoResultadoUrl;
            ValorAtual = resultado.ValorEstimado ?? 0;
            TemResultado = true;

            ComprimentoSelecionado = resultado.Comprimento;
            CorSelecionada = resultado.Cor;
            TipoCabeloSelecionado = resultado.TipoCabelo;
            MetodoSelecionado = resultado.MetodoMegaHair;
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

        FotoExibicao = item.FotoOriginalUrl;
        FotoResultadoUrl = item.FotoResultadoUrl;
        ValorAtual = item.ValorEstimado ?? 0;
        ComprimentoSelecionado = item.Comprimento;
        CorSelecionada = item.Cor;
        TipoCabeloSelecionado = item.TipoCabelo;
        MetodoSelecionado = item.MetodoMegaHair;
        TemResultado = true;
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
            "fita adesiva" => 1800m,
            "queratina" => 2200m,
            "micro link" => 2500m,
            "costurado" => 2800m,
            _ => 1800m
        };
    }
}