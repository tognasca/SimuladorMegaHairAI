using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Services;
using SimuladorMegaHair.Domain.Entities;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

/// <summary>
/// Catálogo de estilos/cores disponíveis, com filtros. Puxa da API
/// (GET /api/catalogo), que já existia no backend mas não era usada
/// pelo app — a tela era só um placeholder "Em breve...".
/// </summary>
public partial class CatalogoViewModel : BaseViewModel
{
    private readonly ApiService _apiService;

    [ObservableProperty]
    private bool carregando;

    [ObservableProperty]
    private string? filtroCor;

    [ObservableProperty]
    private string? filtroComprimento;

    [ObservableProperty]
    private string? filtroTipoCabelo;

    public ObservableCollection<CatalogoItem> Itens { get; } = new();

    public bool TemItens => Itens.Count > 0;
    public bool SemItens => !Carregando && Itens.Count == 0;

    public List<string> ComprimentosDisponiveis { get; } = new()
    {
        "Todos", "45 cm", "55 cm", "65 cm", "75 cm", "85 cm"
    };

    public List<string> TiposDisponiveis { get; } = new()
    {
        "Todos", "Liso", "Ondulado", "Cacheado", "Crespo"
    };

    public CatalogoViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "CATÁLOGO";
        Itens.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(TemItens));
            OnPropertyChanged(nameof(SemItens));
        };
    }

    [RelayCommand]
    private async Task AparecerAsync() => await CarregarAsync();

    [RelayCommand]
    private async Task CarregarAsync()
    {
        try
        {
            Carregando = true;

            var comprimento = FiltroComprimento is null or "Todos" ? null : FiltroComprimento;
            var tipo = FiltroTipoCabelo is null or "Todos" ? null : FiltroTipoCabelo;

            var itens = await _apiService.GetCatalogoAsync(
                cor: string.IsNullOrWhiteSpace(FiltroCor) ? null : FiltroCor,
                comprimento: comprimento,
                tipoCabelo: tipo);

            Itens.Clear();
            foreach (var item in itens)
                Itens.Add(item);

            OnPropertyChanged(nameof(SemItens));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Não foi possível carregar o catálogo: {ex.Message}", "OK");
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private async Task AplicarFiltrosAsync() => await CarregarAsync();

    [RelayCommand]
    private async Task LimparFiltrosAsync()
    {
        FiltroCor = null;
        FiltroComprimento = null;
        FiltroTipoCabelo = null;
        await CarregarAsync();
    }

    /// <summary>
    /// Usa o item do catálogo como ponto de partida de uma nova simulação:
    /// leva para a captura de foto já com o estilo pré-selecionado.
    /// </summary>
    [RelayCommand]
    private async Task SimularComEsteEstiloAsync(CatalogoItem item)
    {
        if (item is null) return;

        var parametros = new Dictionary<string, object>
        {
            ["ComprimentoSugerido"] = item.Comprimento,
            ["CorSugerida"] = item.Cor,
            ["TipoSugerido"] = item.TipoCabelo,
            ["MetodoSugerido"] = item.MetodoMegaHair
        };

        await Shell.Current.GoToAsync("//CapturePage", parametros);
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}
