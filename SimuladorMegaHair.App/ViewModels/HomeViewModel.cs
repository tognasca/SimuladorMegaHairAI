using CommunityToolkit.Mvvm.Input;

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

    [RelayCommand]
    private async Task AbrirConfiguracoesAsync()
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}