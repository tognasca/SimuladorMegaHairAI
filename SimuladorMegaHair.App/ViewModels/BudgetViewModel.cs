using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(ValorEstimado), "ValorEstimado")]
[QueryProperty(nameof(Metodo), "Metodo")]
[QueryProperty(nameof(Comprimento), "Comprimento")]
public partial class BudgetViewModel : BaseViewModel
{
    [ObservableProperty]
    private decimal valorEstimado;

    [ObservableProperty]
    private string metodo = string.Empty;

    [ObservableProperty]
    private string comprimento = string.Empty;

    public BudgetViewModel()
    {
        Title = "Orçamento";
    }

    [RelayCommand]
    private async Task VoltarInicioAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}