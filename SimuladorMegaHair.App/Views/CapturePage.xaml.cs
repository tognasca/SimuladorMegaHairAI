using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class CapturePage : ContentPage
{
    public CapturePage(CaptureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}