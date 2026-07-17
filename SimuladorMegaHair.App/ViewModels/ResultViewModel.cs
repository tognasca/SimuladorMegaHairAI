using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(FotoOriginalUrl), "FotoOriginalUrl")]
[QueryProperty(nameof(FotoResultadoUrl), "FotoResultadoUrl")]
[QueryProperty(nameof(ValorEstimado), "ValorEstimado")]
[QueryProperty(nameof(Metodo), "Metodo")]
[QueryProperty(nameof(Comprimento), "Comprimento")]
public partial class ResultViewModel : BaseViewModel
{
    [ObservableProperty]
    private string fotoOriginalUrl = string.Empty;

    [ObservableProperty]
    private string fotoResultadoUrl = string.Empty;

    [ObservableProperty]
    private decimal valorEstimado;

    [ObservableProperty]
    private string metodo = string.Empty;

    [ObservableProperty]
    private string comprimento = string.Empty;

    public ResultViewModel()
    {
        Title = "Resultado";
    }

    [RelayCommand]
    private async Task GerarOrcamentoAsync()
    {
        var parametros = new Dictionary<string, object>
        {
            ["ValorEstimado"] = ValorEstimado,
            ["Metodo"] = Metodo,
            ["Comprimento"] = Comprimento
        };

        await Shell.Current.GoToAsync("//BudgetPage", parametros);
    }

    [RelayCommand]
    private async Task NovaSimulacaoAsync()
    {
        await Shell.Current.GoToAsync("//CapturePage");
    }
}