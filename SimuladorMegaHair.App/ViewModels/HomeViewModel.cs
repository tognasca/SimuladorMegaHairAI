using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

/// <summary>
/// Tela inicial: agora funciona como um MENU de decisão, em vez de ir
/// direto para a câmera. O usuário escolhe o que quer fazer:
/// nova simulação, buscar cliente com histórico, ver catálogo,
/// orçamentos salvos ou configurações.
/// </summary>
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
    private async Task BuscarClienteAsync()
    {
        await Shell.Current.GoToAsync("//ClientesPage");
    }

    [RelayCommand]
    private async Task AbrirCatalogoAsync()
    {
        await Shell.Current.GoToAsync("//CatalogoPage");
    }

    [RelayCommand]
    private async Task AbrirOrcamentosAsync()
    {
        await Shell.Current.GoToAsync("//OrcamentosPage");
    }

    [RelayCommand]
    private async Task AbrirConfiguracoesAsync()
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}
