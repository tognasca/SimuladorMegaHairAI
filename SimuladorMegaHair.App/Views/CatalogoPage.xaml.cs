using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class CatalogoPage : ContentPage
{
    private readonly CatalogoViewModel _viewModel;

    public CatalogoPage(CatalogoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        AtualizarColunas();
        DeviceDisplay.Current.MainDisplayInfoChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(AtualizarColunas);
    }

    /// <summary>
    /// Ajusta quantos cards de catálogo cabem por linha de acordo com a
    /// largura real da tela — evita cards espremidos numa TV montada em
    /// pé no salão ou num celular, e aproveita bem o espaço numa TV
    /// deitada ou monitor largo.
    /// </summary>
    private void AtualizarColunas()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        if (info.Density <= 0) return;

        var larguraDp = info.Width / info.Density;

        LayoutCatalogo.Span = larguraDp switch
        {
            < 600 => 1,   // celular/TV vertical estreita
            < 900 => 2,   // tablet retrato / TV vertical grande
            < 1300 => 3,  // desktop / TV deitada padrão
            _ => 4         // TV grande / monitor largo
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.AparecerCommand.ExecuteAsync(null);
    }
}