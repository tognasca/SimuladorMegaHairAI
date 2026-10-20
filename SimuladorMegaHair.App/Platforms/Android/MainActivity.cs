using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SimuladorMegaHair.App;

/// <summary>
/// ScreenOrientation.FullUser: aceita retrato E paisagem, girando
/// livremente conforme o sensor do aparelho (ou a orientação física do
/// display, no caso de um totem/TV touch Android montado em pé) — é
/// exatamente o "roda na vertical e na horizontal" pedido.
///
/// ConfigChanges inclui Orientation + ScreenSize + ScreenLayout: sem isso,
/// o Android DESTRÓI e recria a Activity inteira a cada rotação (perdendo
/// estado, e mais lento). Com isso, o MAUI só relayouta a UI existente —
/// é o que faz a troca Portrait/Landscape (StyleSelectionPage) parecer
/// instantânea em vez de a tela "piscar".
/// </summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.FullUser,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
