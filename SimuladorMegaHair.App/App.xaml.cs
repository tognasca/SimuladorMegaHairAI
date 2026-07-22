namespace SimuladorMegaHair.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Mega Hair AI",

            //// Janela vertical para teste no computador.
            //Width = 650,
            //Height = 1050,

            //MinimumWidth = 480,
            //MinimumHeight = 800
        };

        return window;
    }
}