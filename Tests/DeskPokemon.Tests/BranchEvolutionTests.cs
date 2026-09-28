using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task ReturningToABranchingPreformKeepsItSelectedUntilTheRemainingBranchIsRequested()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(265);
        settings.For(265).Level = 7;
        Assert.NotNull(settings.Evolve(265, 266));
        var pendingEgg = settings.PendingEgg;
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
            await EventuallyAsync(window, () => settings.SelectedDex != 266 && !Field<bool>(window, "_evolving"));

            Assert.Equal(265, settings.SelectedDex);
            Assert.Equal(268, Assert.Single(settings.AvailableEvolutions(265)).TargetDex);
            Assert.False(settings.IsOwned(268));
            Assert.True(window.FindControl<Button>("EvolutionNotice")!.IsVisible);
            Assert.Contains("evolvable", grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 265).Classes);
            await InvokeAsync(window, "CheckEvolutionAsync"); // A later automatic check also leaves the preform alone.
            Assert.Equal(265, settings.SelectedDex);
            Assert.False(settings.IsOwned(268));

            Click(window.FindControl<Button>("EvolutionNotice")!); // Explicitly accept the remaining branch.
            await EventuallyAsync(window, () => settings.SelectedDex == 268 && !Field<bool>(window, "_evolving"));
            Assert.True(settings.IsOwned(265));
            Assert.True(settings.IsOwned(266));
            Assert.True(settings.IsOwned(268));
            Assert.Same(pendingEgg, settings.PendingEgg);
        }
        finally { window.Close(); }
    }
}
