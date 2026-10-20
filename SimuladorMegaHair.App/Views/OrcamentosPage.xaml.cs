using Microsoft.Maui.Controls;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class OrcamentosPage : ContentPage
{
    public OrcamentosPage(OrcamentosViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}