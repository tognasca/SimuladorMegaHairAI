using Microsoft.Maui.Controls;
using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class ClientesPage : ContentPage
{
    public ClientesPage(ClientesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}