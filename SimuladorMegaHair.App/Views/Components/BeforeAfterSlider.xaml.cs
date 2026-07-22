using Microsoft.Maui.Controls.Shapes;

namespace SimuladorMegaHair.App.Views.Components;

public partial class BeforeAfterSlider : ContentView
{
    public static readonly BindableProperty BeforeSourceProperty =
        BindableProperty.Create(nameof(BeforeSource), typeof(ImageSource), typeof(BeforeAfterSlider));

    public static readonly BindableProperty AfterSourceProperty =
        BindableProperty.Create(nameof(AfterSource), typeof(ImageSource), typeof(BeforeAfterSlider));

    public ImageSource BeforeSource
    {
        get => (ImageSource)GetValue(BeforeSourceProperty);
        set => SetValue(BeforeSourceProperty, value);
    }

    public ImageSource AfterSource
    {
        get => (ImageSource)GetValue(AfterSourceProperty);
        set => SetValue(AfterSourceProperty, value);
    }

    private double _sliderPosition = 0.5; // Começa no meio (50%)
    private double _startSliderPosition;  // Grava onde o dedo tocou
    private double _containerWidth;
    private double _containerHeight;

    public BeforeAfterSlider()
    {
        InitializeComponent();
    }

    private void OnContainerSizeChanged(object? sender, EventArgs e)
    {
        _containerWidth = ContainerGrid.Width;
        _containerHeight = ContainerGrid.Height;
        AtualizarSlider();
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_containerWidth <= 0) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                // Quando o usuário toca na tela, gravamos a posição atual
                _startSliderPosition = _sliderPosition;
                break;

            case GestureStatus.Running:
                // e.TotalX é a distância total arrastada desde que tocou na tela
                double porcentagemArrastada = e.TotalX / _containerWidth;

                // Calcula a nova posição e trava entre 5% e 95% da tela
                _sliderPosition = Math.Clamp(_startSliderPosition + porcentagemArrastada, 0.05, 0.95);
                AtualizarSlider();
                break;
        }
    }

    private void AtualizarSlider()
    {
        if (_containerWidth <= 0 || _containerHeight <= 0) return;

        double posX = _containerWidth * _sliderPosition;

        // Corta a imagem do "ANTES" para mostrar só até a linha do slider
        ImgAntes.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, posX, _containerHeight)
        };

        // Move a linha e a bolinha (handle)
        LinhaDivisoria.TranslationX = posX - (LinhaDivisoria.Width / 2);
        Handle.TranslationX = posX - (Handle.Width / 2);
    }
}