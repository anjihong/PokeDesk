using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task APreviousBranchAppearanceNeedsANewHatchBeforeGrowingIntoTheRemainingBranch()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(265);
        settings.For(265).Level = 7;
        settings.For(265).Exp = 9;
        Assert.NotNull(settings.Evolve(265, 266));
        var pendingEgg = settings.PendingEgg;
        var originalRun = settings.For(266);
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 266, false));
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 3);
            await WaitForDexAsync(window, 135);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 265).IsChecked = true;
            await EventuallyAsync(window, () => settings.SelectedDex == 265 && !Field<bool>(window, "_evolving") &&
                Field<int>(window, "_pendingSelections") == 0);

            Assert.Equal(265, settings.SelectedDex);
            Assert.Empty(settings.AvailableEvolutions(265));
            Assert.False(settings.IsOwned(268));
            Assert.False(window.FindControl<Button>("EvolutionNotice")!.IsVisible);
            Assert.DoesNotContain("evolvable", grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 265).Classes);
            await InvokeAsync(window, "CheckEvolutionAsync"); // A later automatic check also leaves the preform alone.
            Assert.Equal(265, settings.SelectedDex);
            Assert.False(settings.IsOwned(268));
            Assert.Same(originalRun, settings.For(265));
            Assert.Same(pendingEgg, settings.PendingEgg);

            settings.PendingEgg = new(EggKind.Common, 265, false);
            settings.Eggs = 1;
            SetEggState(window, "Ready");
            await InvokeAsync(window, "HatchAsync").WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(window, () => Field<SpriteAtlas?>(window, "_resultAtlas") != null);
            Field<DispatcherTimer>(window, "_resultTimer").Stop();
            Assert.Equal("새 육성 · Lv.1", window.FindControl<TextBlock>("NewText")!.Text);
            Assert.NotSame(originalRun, settings.For(265));
            Assert.Same(originalRun, settings.For(266));
            Assert.Equal((7, 9), (originalRun.Level, originalRun.Exp));
            Assert.Equal((1, 0), (settings.For(265).Level, settings.For(265).Exp));
            Assert.Equal(new[] { 265 }, settings.For(265).History);
            SetEggState(window, "Waiting");

            settings.For(265).Level = 6;
            settings.For(265).Exp = Settings.ExpToNext(6) - 1;
            Invoke(window, "AddExp"); // The remaining unowned branch is automatic for this new growth run.
            await EventuallyAsync(window, () => settings.SelectedDex == 268 && !Field<bool>(window, "_evolving"));
            Assert.True(settings.IsOwned(265));
            Assert.True(settings.IsOwned(266));
            Assert.True(settings.IsOwned(268));
            Assert.Same(settings.For(265), settings.For(268));
            Assert.NotSame(settings.For(266), settings.For(268));
            Assert.Equal(new[] { 265, 268 }, settings.For(268).History);
            Assert.Equal((7, 9), (originalRun.Level, originalRun.Exp));
        }
        finally { window.Close(); }
    }
}
