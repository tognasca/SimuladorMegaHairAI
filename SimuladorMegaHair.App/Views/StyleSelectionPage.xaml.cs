using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class StyleSelectionPage : ContentPage
{
    public StyleSelectionPage(StyleSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}