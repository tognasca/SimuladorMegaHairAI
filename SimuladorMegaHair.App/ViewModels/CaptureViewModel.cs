using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Services;

namespace SimuladorMegaHair.App.ViewModels;

[QueryProperty(nameof(ClienteId), "ClienteId")]
[QueryProperty(nameof(ClienteNome), "ClienteNome")]
[QueryProperty(nameof(ComprimentoSugerido), "ComprimentoSugerido")]
[QueryProperty(nameof(CorSugerida), "CorSugerida")]
[QueryProperty(nameof(TipoSugerido), "TipoSugerido")]
[QueryProperty(nameof(MetodoSugerido), "MetodoSugerido")]
public partial class CaptureViewModel : BaseViewModel
{
    private readonly CameraService _cameraService;

    [ObservableProperty]
    private string? fotoPath;

    [ObservableProperty]
    private bool temFoto;

    /// <summary>
    /// Quando a simulação parte da tela "Buscar Cliente", o cliente
    /// selecionado é propagado até a criação da simulação, para que ela
    /// já fique vinculada a ele no histórico.
    /// </summary>
    [ObservableProperty]
    private string? clienteId;

    [ObservableProperty]
    private string? clienteNome;

    /// <summary>
    /// Preenchidos quando a simulação parte do Catálogo ("Simular com este
    /// estilo"), para pré-selecionar as opções na tela seguinte.
    /// </summary>
    [ObservableProperty]
    private string? comprimentoSugerido;

    [ObservableProperty]
    private string? corSugerida;

    [ObservableProperty]
    private string? tipoSugerido;

    [ObservableProperty]
    private string? metodoSugerido;

    public bool TemClienteVinculado => !string.IsNullOrWhiteSpace(ClienteNome);

    partial void OnClienteNomeChanged(string? value)
        => OnPropertyChanged(nameof(TemClienteVinculado));

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

                if (!string.IsNullOrWhiteSpace(ClienteId))
                    parametrosTeste["ClienteId"] = ClienteId;

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

        if (!string.IsNullOrWhiteSpace(ClienteId))
            parametros["ClienteId"] = ClienteId;

        if (!string.IsNullOrWhiteSpace(ClienteNome))
            parametros["ClienteNome"] = ClienteNome;

        await Shell.Current.GoToAsync("//StyleSelectionPage", parametros);
    }
}