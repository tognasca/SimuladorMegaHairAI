using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class ClienteDetalhePage : ContentPage
{
    public ClienteDetalhePage(ClienteDetalheViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
