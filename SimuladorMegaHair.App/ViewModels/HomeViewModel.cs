using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    public HomeViewModel()
    {
        Title = "MegaHair AI";
    }

    [RelayCommand]
    private async Task IniciarSimulacaoAsync()
    {
        await Shell.Current.GoToAsync("//CapturePage");
    }
}