using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace SimuladorMegaHair.App.Views.Components;

public partial class LoadingOverlay : ContentView
{
    private readonly string[] _mensagens = new[]
    {
        "Analisando seu rosto...",
        "Selecionando o melhor estilo...",
        "Aplicando o mega hair...",
        "Ajustando os detalhes...",
        "Finalizando sua nova versão..."
    };

    private int _indiceMensagem = 0;
    private IDispatcherTimer? _timerMensagens;
    private IDispatcherTimer? _timerBarra;
    private double _progresso = 0;

    public static readonly BindableProperty IsLoadingProperty =
        BindableProperty.Create(
            nameof(IsLoading),
            typeof(bool),
            typeof(LoadingOverlay),
            false,
            propertyChanged: OnIsLoadingChanged);

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public LoadingOverlay()
    {
        InitializeComponent();
    }

    private static void OnIsLoadingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is LoadingOverlay overlay)
        {
            bool isLoading = (bool)newValue;
            overlay.IsVisible = isLoading;

            if (isLoading)
                overlay.StartAnimations();
            else
                overlay.StopAnimations();
        }
    }

    private void StartAnimations()
    {
        _indiceMensagem = 0;
        _progresso = 0;
        LblMensagem.Text = _mensagens[0];

        // Timer para trocar mensagens a cada 2s
        _timerMensagens = Dispatcher.CreateTimer();
        _timerMensagens.Interval = TimeSpan.FromSeconds(2);
        _timerMensagens.Tick += (_, __) =>
        {
            _indiceMensagem = (_indiceMensagem + 1) % _mensagens.Length;
            LblMensagem.FadeTo(0, 200).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LblMensagem.Text = _mensagens[_indiceMensagem];
                    LblMensagem.FadeTo(1, 200);
                });
            });
        };
        _timerMensagens.Start();

        // Timer para animar a barra de progresso (visual, não real)
        _timerBarra = Dispatcher.CreateTimer();
        _timerBarra.Interval = TimeSpan.FromMilliseconds(80);
        _timerBarra.Tick += (_, __) =>
        {
            _progresso += 3;
            if (_progresso > 240) _progresso = 20;
            BarraProgresso.WidthRequest = _progresso;
        };
        _timerBarra.Start();

        // Fade-in do overlay
        this.Opacity = 0;
        this.FadeTo(1, 250);
    }

    private void StopAnimations()
    {
        _timerMensagens?.Stop();
        _timerMensagens = null;

        _timerBarra?.Stop();
        _timerBarra = null;
    }
}