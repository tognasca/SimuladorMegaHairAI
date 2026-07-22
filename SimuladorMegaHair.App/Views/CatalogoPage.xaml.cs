using Microsoft.Maui.Controls;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class CatalogoPage : ContentPage
{
    public CatalogoPage(CatalogoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}