using System;

namespace SimuladorMegaHair.App.Views.Components;

public partial class LoadingOverlay : ContentView
{
    private readonly string[] _mensagens = new[]
    {
        "Analisando seu rosto...",
        "Detectando região do cabelo...",
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

        // Começa escondido
        this.IsVisible = false;
        this.InputTransparent = true;
    }

    private static void OnIsLoadingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is LoadingOverlay overlay)
        {
            bool isLoading = (bool)newValue;

            Console.WriteLine($"[LoadingOverlay] IsLoading mudou para: {isLoading}");

            // Força na UI thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                overlay.IsVisible = isLoading;
                overlay.InputTransparent = !isLoading;

                if (isLoading)
                    overlay.StartAnimations();
                else
                    overlay.StopAnimations();
            });
        }
    }

    private void StartAnimations()
    {
        Console.WriteLine("[LoadingOverlay] START animations");

        _indiceMensagem = 0;
        _progresso = 0;
        LblMensagem.Text = _mensagens[0];

        _timerMensagens = Dispatcher.CreateTimer();
        _timerMensagens.Interval = TimeSpan.FromSeconds(2);
        _timerMensagens.Tick += (_, __) =>
        {
            _indiceMensagem = (_indiceMensagem + 1) % _mensagens.Length;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LblMensagem.Text = _mensagens[_indiceMensagem];
            });
        };
        _timerMensagens.Start();

        _timerBarra = Dispatcher.CreateTimer();
        _timerBarra.Interval = TimeSpan.FromMilliseconds(80);
        _timerBarra.Tick += (_, __) =>
        {
            _progresso += 3;
            if (_progresso > 240) _progresso = 20;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BarraProgresso.WidthRequest = _progresso;
            });
        };
        _timerBarra.Start();
    }

    private void StopAnimations()
    {
        Console.WriteLine("[LoadingOverlay] STOP animations");

        _timerMensagens?.Stop();
        _timerMensagens = null;

        _timerBarra?.Stop();
        _timerBarra = null;
    }
}