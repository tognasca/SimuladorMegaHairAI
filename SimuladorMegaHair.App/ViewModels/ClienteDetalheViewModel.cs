using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuladorMegaHair.App.Services;
using SimuladorMegaHair.Domain.DTOs;
using System.Collections.ObjectModel;

namespace SimuladorMegaHair.App.ViewModels;

/// <summary>
/// Detalhe de um cliente: dados de contato + histórico completo de
/// simulações (fotos antes/depois), com opção de reabrir qualquer uma
/// delas ou iniciar uma simulação nova para o mesmo cliente.
/// </summary>
[QueryProperty(nameof(ClienteId), "ClienteId")]
public partial class ClienteDetalheViewModel : BaseViewModel
{
    private readonly ApiService _apiService;

    [ObservableProperty]
    private string? clienteId;

    [ObservableProperty]
    private bool carregando;

    [ObservableProperty]
    private bool editando;

    [ObservableProperty]
    private string nome = string.Empty;

    [ObservableProperty]
    private string telefone = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private DateTime criadoEm;

    public ObservableCollection<SimulacaoResponse> Simulacoes { get; } = new();

    public bool TemHistorico => Simulacoes.Count > 0;

    public ClienteDetalheViewModel(ApiService apiService)
    {
        _apiService = apiService;
        Title = "CLIENTE";
        Simulacoes.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TemHistorico));
    }

    partial void OnClienteIdChanged(string? value)
    {
        _ = CarregarAsync();
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (string.IsNullOrWhiteSpace(ClienteId) || !Guid.TryParse(ClienteId, out var id))
            return;

        try
        {
            Carregando = true;
            var detalhe = await _apiService.ObterClienteAsync(id);
            if (detalhe is null) return;

            Nome = detalhe.Nome;
            Telefone = detalhe.Telefone ?? string.Empty;
            Email = detalhe.Email ?? string.Empty;
            CriadoEm = detalhe.CriadoEm;
            Title = detalhe.Nome;

            Simulacoes.Clear();
            foreach (var s in detalhe.Simulacoes)
                Simulacoes.Add(s);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Não foi possível carregar o cliente: {ex.Message}", "OK");
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private void AlternarEdicao() => Editando = !Editando;

    [RelayCommand]
    private async Task SalvarEdicaoAsync()
    {
        if (string.IsNullOrWhiteSpace(ClienteId) || !Guid.TryParse(ClienteId, out var id))
            return;

        if (string.IsNullOrWhiteSpace(Nome))
        {
            await Shell.Current.DisplayAlert("Atenção", "O nome não pode ficar vazio.", "OK");
            return;
        }

        try
        {
            Carregando = true;
            var atualizado = await _apiService.AtualizarClienteAsync(id, new AtualizarClienteRequest
            {
                Nome = Nome,
                Telefone = Telefone,
                Email = Email
            });

            if (atualizado is not null)
            {
                Title = atualizado.Nome;
                Editando = false;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Erro", $"Não foi possível salvar: {ex.Message}", "OK");
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>
    /// Inicia uma nova simulação, já vinculada a este cliente.
    /// </summary>
    [RelayCommand]
    private async Task NovaSimulacaoAsync()
    {
        var parametros = new Dictionary<string, object>
        {
            ["ClienteId"] = ClienteId ?? string.Empty,
            ["ClienteNome"] = Nome
        };

        await Shell.Current.GoToAsync("//CapturePage", parametros);
    }

    /// <summary>
    /// Reabre uma simulação antiga na tela de resultado, para o cliente
    /// ver de novo ou continuar ajustando o volume.
    /// </summary>
    [RelayCommand]
    private async Task ReabrirSimulacaoAsync(SimulacaoResponse simulacao)
    {
        if (simulacao is null) return;

        var parametros = new Dictionary<string, object>
        {
            ["FotoPath"] = simulacao.FotoOriginalUrl,
            ["ClienteId"] = ClienteId ?? string.Empty,
            ["ClienteNome"] = Nome
        };

        // StyleSelectionPage carrega o histórico da própria foto ao entrar;
        // como a foto original é a mesma, essa simulação específica aparece
        // na lista "Histórico" da tela, pronta para ser selecionada/reaberta.
        await Shell.Current.GoToAsync("//StyleSelectionPage", parametros);
    }

    [RelayCommand]
    private async Task VoltarAsync()
    {
        await Shell.Current.GoToAsync("//ClientesPage");
    }
}
