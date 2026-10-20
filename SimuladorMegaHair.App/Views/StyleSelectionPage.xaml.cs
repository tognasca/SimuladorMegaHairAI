using SimuladorMegaHair.App.ViewModels;

namespace SimuladorMegaHair.App.Views;

public partial class StyleSelectionPage : ContentPage
{
    private StyleSelectionViewModel ViewModel =>
        (StyleSelectionViewModel)BindingContext;

    public StyleSelectionPage(
        StyleSelectionViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;

        SizeChanged += OnPageSizeChanged;
    }

    private void OnPageSizeChanged(
        object? sender,
        EventArgs e)
    {
        ViewModel.AtualizarLayout(
            Width,
            Height);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ViewModel.AtualizarLayout(
            Width,
            Height);
    }
}