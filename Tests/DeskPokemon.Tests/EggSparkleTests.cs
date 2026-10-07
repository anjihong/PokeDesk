using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData("Waiting")]
    [InlineData("Ready")]
    public async Task ShinyEggSparklesLoopOnlyDuringWaitingOrReadyAndStopOnClose(string state)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Shiny, 4, true);
        var window = new MainWindow(settings, false);
        try
        {
            window.Show();
            SetEggState(window, state);
            await InvokeAsync(window, "LoadEggAssetsAsync");
            var effect = window.FindControl<Image>("EggSparkles")!;
            var frames = EggArtwork.LoadSparkles();
            var timer = Field<DispatcherTimer>(window, "_eggSparkleTimer");
            Assert.True(effect.IsVisible);
            Assert.False(effect.IsHitTestVisible);
            Assert.Equal((40d, 38d, -6d, -6d), (effect.Width, effect.Height, Canvas.GetLeft(effect), Canvas.GetTop(effect)));
            Assert.Same(frames[0].Bitmap, effect.Source);
            Assert.Equal(TimeSpan.FromMilliseconds(100), timer.Interval);
            Assert.True(timer.IsEnabled);
            timer.Stop(); // Advance the real callback deterministically without a wall-clock race.
            for (var i = 1; i <= frames.Length; i++)
            {
                Invoke(window, "AdvanceEggSparkles");
                Assert.Same(frames[i % frames.Length].Bitmap, effect.Source);
            }
            Invoke(window, "StartEggSparkles", (object)frames);
            Assert.True(timer.IsEnabled);
            window.Close();
            Assert.False(timer.IsEnabled);
            Assert.False(effect.IsVisible);
            Assert.Null(effect.Source);
            Assert.Null(Field<DispatcherTimer?>(window, "_eggSparkleTimer"));
            Assert.NotNull(SpritePixels.CopyFrom(frames[0].Bitmap)); // Closing a borrower leaves cached art alive.
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Hatching")]
    [InlineData("Result")]
    public async Task HatchAndResultClearSparklesAndCannotRestartThem(string state)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Shiny, 4, true);
        var window = new MainWindow(settings, false);
        try
        {
            window.Show();
            SetEggState(window, "Ready");
            await InvokeAsync(window, "LoadEggAssetsAsync");
            var effect = window.FindControl<Image>("EggSparkles")!;
            var timer = Field<DispatcherTimer>(window, "_eggSparkleTimer");
            Assert.True(effect.IsVisible);
            SetEggState(window, state);
            Assert.False(timer.IsEnabled);
            Assert.False(effect.IsVisible);
            Assert.Null(effect.Source);
            // A stale callback, or accidental late load, must not restore a waiting effect.
            Invoke(window, "AdvanceEggSparkles");
            await InvokeAsync(window, "LoadEggAssetsAsync");
            Assert.False(effect.IsVisible);
            Assert.Null(effect.Source);
            Assert.False(timer.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(EggKind.Common)]
    [InlineData(EggKind.Rare)]
    [InlineData(EggKind.Epic)]
    [InlineData(EggKind.Legendary)]
    public async Task ReplacingShinyEggWithAnotherTierKeepsThatTierEvenForAGuaranteedShinyReward(EggKind kind)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Shiny, 4, true);
        var window = new MainWindow(settings, false);
        try
        {
            window.Show();
            SetEggState(window, "Ready");
            await InvokeAsync(window, "LoadEggAssetsAsync");
            var effect = window.FindControl<Image>("EggSparkles")!;
            var timer = Field<DispatcherTimer>(window, "_eggSparkleTimer");
            Assert.True(effect.IsVisible);

            settings.PendingEgg = new(kind, 4, true);
            SetEggState(window, "Ready");
            Assert.False(effect.IsVisible);
            Assert.Null(effect.Source);
            await InvokeAsync(window, "LoadEggAssetsAsync");
            Assert.Same((await EggArtwork.LoadAsync(kind)).Bitmap, window.FindControl<Image>("EggImage")!.Source);
            Assert.False(effect.IsVisible);
            Assert.Null(effect.Source);
            Assert.False(timer.IsEnabled);
        }
        finally { window.Close(); }
    }
}
