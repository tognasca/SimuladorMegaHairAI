using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

public partial class ClientesViewModel : BaseViewModel
{
    public ClientesViewModel()
    {
        Title = "CLIENTES";
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}