using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SimuladorMegaHair.Web.Services;

/// <summary>
/// Guarda o token JWT da sessão atual (em memória, por circuito Blazor) e
/// o persiste em Local Storage do navegador, para sobreviver a um F5 ou a
/// reconexão do circuito — comum em TV/tablet do salão que fica ligado o
/// dia inteiro. Não é compartilhado entre dispositivos: cada aparelho
/// faz login uma vez e guarda o próprio token localmente.
/// </summary>
public class AuthTokenStore
{
    private const string ChaveStorage = "megahair.auth.token";
    private readonly ProtectedLocalStorage _storage;

    public string? Token { get; private set; }
    public DateTime? ExpiraEm { get; private set; }
    public string? EmailUsuario { get; private set; }

    public event Action? OnChange;

    public AuthTokenStore(ProtectedLocalStorage storage)
    {
        _storage = storage;
    }

    public bool EstaAutenticado => !string.IsNullOrWhiteSpace(Token) &&
                                    ExpiraEm is not null && ExpiraEm > DateTime.UtcNow;

    /// <summary>Tenta restaurar um token salvo anteriormente neste navegador.</summary>
    public async Task<bool> RestaurarAsync()
    {
        try
        {
            var resultado = await _storage.GetAsync<TokenPersistido>(ChaveStorage);
            if (!resultado.Success || resultado.Value is null)
                return false;

            if (resultado.Value.ExpiraEm <= DateTime.UtcNow)
            {
                await LimparAsync();
                return false;
            }

            Token = resultado.Value.Token;
            ExpiraEm = resultado.Value.ExpiraEm;
            EmailUsuario = resultado.Value.Email;
            OnChange?.Invoke();
            return true;
        }
        catch
        {
            // Local Storage pode não estar disponível ainda durante o
            // pré-render estático — trate como "não autenticado" nesse caso.
            return false;
        }
    }

    public async Task DefinirAsync(string token, DateTime expiraEm, string email)
    {
        Token = token;
        ExpiraEm = expiraEm;
        EmailUsuario = email;

        await _storage.SetAsync(ChaveStorage, new TokenPersistido(token, expiraEm, email));
        OnChange?.Invoke();
    }

    public async Task LimparAsync()
    {
        Token = null;
        ExpiraEm = null;
        EmailUsuario = null;

        try { await _storage.DeleteAsync(ChaveStorage); } catch { /* ignora */ }
        OnChange?.Invoke();
    }

    private record TokenPersistido(string Token, DateTime ExpiraEm, string Email);
}
