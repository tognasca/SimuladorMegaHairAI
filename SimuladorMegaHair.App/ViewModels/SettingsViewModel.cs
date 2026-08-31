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

    /// <summary>
    /// Endereço do servidor (API). Era fixo em "http://localhost:5185/",
    /// o que só funciona quando o app roda NO MESMO computador que a API —
    /// quebra completamente em qualquer tablet/celular/TV que não seja essa
    /// máquina. Agora é configurável: aponte todos os dispositivos do salão
    /// (TV + tablets + celulares das clientes) para o IP da máquina que
    /// roda a API, e todos compartilham a mesma base de clientes/catálogo.
    /// </summary>
    [ObservableProperty]
    private string apiBaseUrl;

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
            false);

        imagemDeTesteSelecionada = Preferences.Get(
            "ImagemDeTeste",
            "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png");

        apiBaseUrl = Preferences.Get(
            "ApiBaseUrl",
            "http://localhost:5185/");
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

    partial void OnApiBaseUrlChanged(string value)
    {
        var normalizado = value.Trim();
        if (!normalizado.EndsWith('/'))
            normalizado += "/";

        Preferences.Set("ApiBaseUrl", normalizado);
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}