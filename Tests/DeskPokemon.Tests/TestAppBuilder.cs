using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;

[assembly: AvaloniaTestApplication(typeof(DeskPokemon.Tests.TestAppBuilder))]

namespace DeskPokemon.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .With(new FontManagerOptions { DefaultFamilyName = "avares://DeskPokemon/Assets/Fonts#NanumGothic" })
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
