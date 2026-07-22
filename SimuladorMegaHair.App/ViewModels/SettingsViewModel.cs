using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace SimuladorMegaHair.App.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    [ObservableProperty]
    private string layoutModeSelected;

    [ObservableProperty]
    private string modoOperacaoSelecionado;

    [ObservableProperty]
    private bool usarImagemDeTeste;

    [ObservableProperty]
    private string imagemDeTesteSelecionada;

    public IReadOnlyList<string> LayoutModes { get; } =
    [
        "Automático",
        "Vertical",
        "Horizontal"
    ];

    public IReadOnlyList<string> ModosOperacao { get; } =
    [
        "Cliente (TV)",
        "Operador (Tablet)"
    ];

    public IReadOnlyList<string> ImagensDisponiveis { get; } =
    [
        "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png",
        "outra-imagem.png" // Adicione mais imagens de teste aqui
    ];

    public SettingsViewModel()
    {
        Title = "CONFIGURAÇÕES";

        layoutModeSelected = Preferences.Get(
            "LayoutMode",
            "Automático");

        modoOperacaoSelecionado = Preferences.Get(
            "ModoOperacao",
            "Cliente (TV)");

        usarImagemDeTeste = Preferences.Get(
            "UsarImagemDeTeste",
            true);

        imagemDeTesteSelecionada = Preferences.Get(
            "ImagemDeTeste",
            "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png");
    }

    partial void OnLayoutModeSelectedChanged(string value)
    {
        Preferences.Set("LayoutMode", value);
    }

    partial void OnModoOperacaoSelecionadoChanged(string value)
    {
        Preferences.Set("ModoOperacao", value);
    }

    partial void OnUsarImagemDeTesteChanged(bool value)
    {
        Preferences.Set("UsarImagemDeTeste", value);
    }

    partial void OnImagemDeTesteSelecionadaChanged(string value)
    {
        Preferences.Set("ImagemDeTeste", value);
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}