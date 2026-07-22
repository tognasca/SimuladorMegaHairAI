using Microsoft.Maui.Storage;

namespace SimuladorMegaHair.App.Services;

public static class AppSettings
{
    public static bool UsarImagemDeTeste =>
        Preferences.Get("UsarImagemDeTeste", true);

    public static string ImagemDeTeste =>
        Preferences.Get("ImagemDeTeste", "e9897c5a-dab8-4c12-b89f-b742a931d9c8.png");

    public static string ApiBaseUrl =>
        "http://localhost:5185/";

    public static string StaticResultUrl =>
        $"{ApiBaseUrl}resultados/{ImagemDeTeste}";
}