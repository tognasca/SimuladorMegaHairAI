using Microsoft.Maui.Controls;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
    }
}