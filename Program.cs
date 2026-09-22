using Avalonia;
using Avalonia.Media;

namespace DeskPokemon;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .With(new FontManagerOptions
        {
            DefaultFamilyName = "avares://DeskPokemon/Assets/Fonts#NanumGothic",
        })
        .LogToTrace();
}
