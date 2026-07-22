using Microsoft.Maui.Controls;

namespace SimuladorMegaHair.App.Views.Components;

public partial class SidebarMenu : ContentView
{
    public SidebarMenu()
    {
        InitializeComponent();
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