using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace SimuladorMegaHair.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}