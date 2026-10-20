using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Services;
using SimuladorMegaHair.Domain.DTOs;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

/// <summary>
/// Tela "Buscar Cliente que já fez simulação". Também permite cadastrar
/// um cliente novo rapidamente antes de uma simulação.
/// </summary>
public partial class ClientesViewModel : BaseViewModel
{
    private readonly ApiService _apiService;
    private CancellationTokenSource? _buscaCts;

    [ObservableProperty]
    private string termoBusca = string.Empty;

    [ObservableProperty]
    private bool carregando;

    [ObservableProperty]
    private bool mostrandoFormularioNovo;

    [ObservableProperty]
    private string novoNome = string.Empty;

    [ObservableProperty]
    private string novoTelefone = string.Empty;

    [ObservableProperty]
    private string novoEmail = string.Empty;

    public ObservableCollection<ClienteResponse> Clientes { get; } = new();

    public bool TemResultados => Clientes.Count > 0;
    public bool SemResultados => !Carregando && Clientes.Count == 0 && !string.IsNullOrWhiteSpace(TermoBusca);

    public ClientesViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "CLIENTES";
        Clientes.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(TemResultados));
            OnPropertyChanged(nameof(SemResultados));
        };
    }

    [RelayCommand]
    private async Task AparecerAsync()
    {
        // Carrega os clientes mais recentes assim que a tela abre,
        // para não deixar a lista vazia sem contexto.
        await BuscarAsync();
    }

    partial void OnTermoBuscaChanged(string value)
    {
        OnPropertyChanged(nameof(SemResultados));
        _ = DebounceBuscarAsync();
    }

    private async Task DebounceBuscarAsync()
    {
        _buscaCts?.Cancel();
        var cts = new CancellationTokenSource();
        _buscaCts = cts;

        try
        {
            await Task.Delay(350, cts.Token);
            if (!cts.IsCancellationRequested)
                await BuscarAsync();
        }
        catch (TaskCanceledException) { /* usuário continuou digitando */ }
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        try
        {
            Carregando = true;
            var resultado = await _apiService.BuscarClientesAsync(TermoBusca);

            Clientes.Clear();
            foreach (var c in resultado)
                Clientes.Add(c);

            OnPropertyChanged(nameof(SemResultados));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Não foi possível buscar clientes: {ex.Message}", "OK");
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private async Task AbrirDetalheAsync(ClienteResponse cliente)
    {
        if (cliente is null) return;

        var parametros = new Dictionary<string, object>
        {
            ["ClienteId"] = cliente.Id.ToString()
        };

        await Shell.Current.GoToAsync("//ClienteDetalhePage", parametros);
    }

    /// <summary>
    /// Inicia uma nova simulação já vinculada a este cliente.
    /// </summary>
    [RelayCommand]
    private async Task NovaSimulacaoParaClienteAsync(ClienteResponse cliente)
    {
        if (cliente is null) return;

        var parametros = new Dictionary<string, object>
        {
            ["ClienteId"] = cliente.Id.ToString(),
            ["ClienteNome"] = cliente.Nome
        };

        await Shell.Current.GoToAsync("//CapturePage", parametros);
    }

    [RelayCommand]
    private void AlternarFormularioNovo()
    {
        MostrandoFormularioNovo = !MostrandoFormularioNovo;
        if (MostrandoFormularioNovo)
        {
            NovoNome = TermoBusca; // aproveita o que a pessoa já digitou na busca
            NovoTelefone = string.Empty;
            NovoEmail = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SalvarNovoClienteAsync()
    {
        if (string.IsNullOrWhiteSpace(NovoNome))
        {
            await Shell.Current.DisplayAlert("Atenção", "Informe o nome do cliente.", "OK");
            return;
        }

        try
        {
            Carregando = true;
            var criado = await _apiService.CriarClienteAsync(new CriarClienteRequest
            {
                Nome = NovoNome,
                Telefone = NovoTelefone,
                Email = NovoEmail
            });

            MostrandoFormularioNovo = false;

            if (criado is not null)
            {
                // Já leva direto para iniciar a simulação desse cliente novo.
                await NovaSimulacaoParaClienteAsync(criado);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Não foi possível salvar o cliente: {ex.Message}", "OK");
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//HomePage");
    }
}
