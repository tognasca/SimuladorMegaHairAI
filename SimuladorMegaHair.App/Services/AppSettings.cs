using Microsoft.Maui.Storage;

namespace SimuladorMegaHair.App.Services;

public static class AppSettings
{
    public static bool UsarImagemDeTeste =>
        Preferences.Get("UsarImagemDeTeste", false);

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

    public static string StaticResultUrl =>
        $"{ApiBaseUrl}resultados/{ImagemDeTeste}";
}