using Microsoft.Maui.Storage;

namespace SimuladorMegaHair.App.Services;

public static class AppSettings
{
    // FASE 1: em builds de Release (o que vai para a loja/instalação no
    // salão) este atalho fica sempre desligado, mesmo que a preferência
    // tenha sido ativada antes — antes, dava para deixar ligado "sem
    // querer" em produção, e a cliente via sempre a mesma foto de exemplo
    // em vez do resultado real. Em DEBUG continua controlável em
    // Configurações, para facilitar o trabalho de layout.
    public static bool UsarImagemDeTeste =>
#if DEBUG
        Preferences.Get("UsarImagemDeTeste", false);
#else
        false;
#endif

    public static string ImagemDeTeste =>
        Preferences.Get("ImagemDeTeste", "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png");

    /// <summary>
    /// Endereço do servidor da API. Configurável em Configurações →
    /// Endereço do servidor. Todos os dispositivos do salão (TV, tablets,
    /// celulares) devem apontar para o mesmo endereço, para compartilhar
    /// a mesma base de clientes/catálogo/histórico.
    /// </summary>
    public static string ApiBaseUrl =>
        Preferences.Get("ApiBaseUrl", "http://localhost:5185/");

    /// <summary>FASE 1: enviada em todas as chamadas como X-Api-Key.</summary>
    public static string ApiKey =>
        Preferences.Get("ApiKey", string.Empty);

    public static string StaticResultUrl =>
        $"{ApiBaseUrl}resultados/{ImagemDeTeste}";
}