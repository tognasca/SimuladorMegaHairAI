using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Models;
using SimuladorMegaHair.App.Services;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(FotoPath), "FotoPath")]
public partial class StyleSelectionViewModel : BaseViewModel
{
    private readonly ApiService _apiService;

    [ObservableProperty]
    private string fotoPath = string.Empty;

    [ObservableProperty]
    private string comprimentoSelecionado = "Longo";

    [ObservableProperty]
    private string corSelecionada = "Castanho Escuro";

    [ObservableProperty]
    private string tipoCabeloSelecionado = "Liso";

    [ObservableProperty]
    private string metodoSelecionado = "Fita Adesiva";

    public ObservableCollection<string> Comprimentos { get; } = new(OpcoesVisual.Comprimentos);
    public ObservableCollection<string> Cores { get; } = new(OpcoesVisual.Cores);
    public ObservableCollection<string> TiposCabelo { get; } = new(OpcoesVisual.TiposCabelo);
    public ObservableCollection<string> Metodos { get; } = new(OpcoesVisual.Metodos);

    public StyleSelectionViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "Escolha o visual";
    }

    [RelayCommand]
    private async Task GerarSimulacaoAsync()
    {
        if (string.IsNullOrWhiteSpace(FotoPath))
        {
            await Shell.Current.DisplayAlert("Erro", "Foto não encontrada.", "OK");
            return;
        }

        try
        {
            IsBusy = true;

            var caminhoServidor = await _apiService.UploadFotoAsync(FotoPath);

            var request = new CriarSimulacaoRequest
            {
                FotoOriginalPath = caminhoServidor,
                Comprimento = ComprimentoSelecionado,
                Cor = CorSelecionada,
                TipoCabelo = TipoCabeloSelecionado,
                MetodoMegaHair = MetodoSelecionado
            };

            var resultado = await _apiService.CriarSimulacaoAsync(request);

            if (resultado is null)
            {
                await Shell.Current.DisplayAlert("Erro", "Falha ao gerar simulação.", "OK");
                return;
            }

            var parametros = new Dictionary<string, object>
            {
                ["FotoOriginalUrl"] = resultado.FotoOriginalUrl,
                ["FotoResultadoUrl"] = resultado.FotoResultadoUrl,
                ["ValorEstimado"] = resultado.ValorEstimado ?? 0m,
                ["Metodo"] = MetodoSelecionado,
                ["Comprimento"] = ComprimentoSelecionado
            };

            await Shell.Current.GoToAsync("//ResultPage", parametros);
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
}