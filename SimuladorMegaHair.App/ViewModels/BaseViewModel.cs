using CommunityToolkit.Mvvm.ComponentModel;

namespace SimuladorMegaHair.App.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        Console.WriteLine($"[BaseViewModel] IsBusy mudou para: {value}");
    }
}