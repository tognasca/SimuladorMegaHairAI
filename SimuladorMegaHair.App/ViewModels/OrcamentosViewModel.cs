using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

public partial class OrcamentosViewModel : BaseViewModel
{
    public OrcamentosViewModel()
    {
        Title = "ORÇAMENTOS";
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}