using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Services;

namespace SimuladorMegaHair.App.ViewModels;

public partial class CaptureViewModel : BaseViewModel
{
    private readonly CameraService _cameraService;

    [ObservableProperty]
    private string? fotoPath;

    [ObservableProperty]
    private bool temFoto;

    public CaptureViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        Title = "Nova Simulação";
    }

    [RelayCommand]
    private async Task TirarFotoAsync()
    {
        var caminho = await _cameraService.TirarFotoAsync();

        if (!string.IsNullOrWhiteSpace(caminho))
        {
            FotoPath = caminho;
            TemFoto = true;
        }
    }

    [RelayCommand]
    private async Task SelecionarDaGaleriaAsync()
    {
        var caminho = await _cameraService.SelecionarDaGaleriaAsync();

        if (!string.IsNullOrWhiteSpace(caminho))
        {
            FotoPath = caminho;
            TemFoto = true;
        }
    }

    [RelayCommand]
    private async Task AvancarAsync()
    {
        if (string.IsNullOrWhiteSpace(FotoPath))
        {
            if (AppSettings.UsarImagemDeTeste)
            {
                var parametrosTeste = new Dictionary<string, object>
                {
                    ["FotoPath"] = AppSettings.StaticResultUrl
                };

                await Shell.Current.GoToAsync(
                    "//StyleSelectionPage",
                    parametrosTeste);

                return;
            }
            await Shell.Current.DisplayAlert("Atenção", "Tire ou selecione uma foto.", "OK");
            return;
        }

        var parametros = new Dictionary<string, object>
        {
            ["FotoPath"] = FotoPath
        };

        await Shell.Current.GoToAsync("//StyleSelectionPage", parametros);
    }
}