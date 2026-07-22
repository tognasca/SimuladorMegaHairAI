using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
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

        builder.Services.AddSingleton(sp =>
        {
#if DEBUG
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost:5185/"),
                Timeout = TimeSpan.FromMinutes(5)
            };
#else
            return new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5185/"),
                Timeout = TimeSpan.FromMinutes(5)
            };
#endif
        });

        // Services
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<CameraService>();

        // ViewModels
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CaptureViewModel>();
        builder.Services.AddTransient<StyleSelectionViewModel>();
        builder.Services.AddTransient<BudgetViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<CatalogoViewModel>();
        builder.Services.AddTransient<ClientesViewModel>();
        builder.Services.AddTransient<OrcamentosViewModel>();

        // Pages
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<CapturePage>();
        builder.Services.AddTransient<StyleSelectionPage>();
        builder.Services.AddTransient<BudgetPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<CatalogoPage>();
        builder.Services.AddTransient<ClientesPage>();
        builder.Services.AddTransient<OrcamentosPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}