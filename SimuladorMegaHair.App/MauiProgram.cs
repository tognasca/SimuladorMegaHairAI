using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SimuladorMegaHair.App.Services;
using SimuladorMegaHair.App.ViewModels;
using SimuladorMegaHair.App.Views;

namespace SimuladorMegaHair.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // HttpClient configurado para a API local
        builder.Services.AddSingleton(sp => new HttpClient
        {
            // ⚠️ IMPORTANTE:
            // Se rodar como Windows App: use "https://localhost:7064/"
            // Se rodar em Android emulador: use "https://10.0.2.2:7064/"
            // Se for tablet físico: use IP da máquina, ex: "http://192.168.0.100:5185/"
            BaseAddress = new Uri("https://localhost:7064/")
        });

        // Services
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<CameraService>();

        // ViewModels
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CaptureViewModel>();
        builder.Services.AddTransient<StyleSelectionViewModel>();
        builder.Services.AddTransient<ResultViewModel>();
        builder.Services.AddTransient<BudgetViewModel>();

        // Pages
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<CapturePage>();
        builder.Services.AddTransient<StyleSelectionPage>();
        builder.Services.AddTransient<ResultPage>();
        builder.Services.AddTransient<BudgetPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}