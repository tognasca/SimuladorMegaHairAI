using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class HomePage : ContentPage
{
    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}