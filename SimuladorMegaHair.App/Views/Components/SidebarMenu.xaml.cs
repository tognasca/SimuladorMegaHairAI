using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace SimuladorMegaHair.App.Views.Components;

/// <summary>
/// Menu lateral compartilhado por Clientes / Catálogo / Detalhe do Cliente.
///
/// RESPONSIVO: em telas largas (TV deitada, desktop, tablet em paisagem)
/// mostra o menu completo com ícone + texto (220px). Em telas estreitas
/// (TV montada em pé no salão, celular, tablet em retrato) encolhe
/// automaticamente para uma barra só de ícones (78px), preservando a
/// navegação sem espremer o conteúdo principal da tela.
///
/// A decisão é tomada com base no tamanho REAL da tela (DeviceDisplay),
/// não no espaço que o Grid hospedeiro concede — por isso as páginas que
/// usam este componente devem declarar a primeira coluna como "Auto"
/// (não um valor fixo), para que o menu possa realmente encolher.
/// </summary>
public partial class SidebarMenu : ContentView
{
    private const double LarguraCompacta = 78;
    private const double LarguraCompleta = 220;
    private const double BreakpointDp = 700; // abaixo disso: modo compacto

    public SidebarMenu()
    {
        InitializeComponent();

        AtualizarModo();

        DeviceDisplay.Current.MainDisplayInfoChanged += OnDisplayInfoChanged;
        Unloaded += (_, __) => DeviceDisplay.Current.MainDisplayInfoChanged -= OnDisplayInfoChanged;
    }

    private void OnDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(AtualizarModo);

    private void AtualizarModo()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        if (info.Density <= 0) return;

        var larguraDp = info.Width / info.Density;
        var alturaDp = info.Height / info.Density;

        // Estreito o suficiente OU claramente em pé (retrato) → compacto.
        bool compacto = larguraDp < BreakpointDp || alturaDp > larguraDp;

        RootBorder.WidthRequest = compacto ? LarguraCompacta : LarguraCompleta;

        LabelInicio.IsVisible = !compacto;
        LabelSimulacao.IsVisible = !compacto;
        LabelCatalogo.IsVisible = !compacto;
        LabelClientes.IsVisible = !compacto;
        LabelOrcamentos.IsVisible = !compacto;
        LabelConfiguracoes.IsVisible = !compacto;
        SeloExperiencia.IsVisible = !compacto;
        LogoTitulo.IsVisible = !compacto;
    }

    private async void OnInicioTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//HomePage");
    }

    private async void OnSimulacaoTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//CapturePage");
    }

    private async void OnCatalogoTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//CatalogoPage");
    }

    private async void OnClientesTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ClientesPage");
    }

    private async void OnOrcamentosTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//OrcamentosPage");
    }

    private async void OnConfiguracoesTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}
