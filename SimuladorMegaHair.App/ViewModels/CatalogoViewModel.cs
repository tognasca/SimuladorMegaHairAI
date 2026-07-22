using CommunityToolkit.Mvvm.Input;

namespace SimuladorMegaHair.App.ViewModels;

public partial class CatalogoViewModel : BaseViewModel
{
    public CatalogoViewModel()
    {
        Title = "CATÁLOGO";
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}