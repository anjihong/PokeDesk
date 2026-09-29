using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task HighLevelShinyEvolutionAutomaticallyAdvancesBothStagesAndPersistsEachSharedGrowthStep()
    {
        using var assets = new UiAssets();
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-shiny-evolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var settings = Settings.NewAt(4, path);
        foreach (var (dex, level, exp) in new[] { (4, 45, 123), (5, 28, 87), (6, 62, 234) })
        {
            settings.Owned.Add(dex);
            settings.For(dex).Level = level;
            settings.For(dex).Exp = exp;
        }
        settings.ShinyOwned.Add(4);
        settings.For(4, true).Level = 36;
        settings.For(4, true).Exp = 71;
        settings.SelectedShiny = true;
        settings.Save();
        var normalOwned = settings.Owned.Order().ToArray();
        var normalProgress = JsonSerializer.Serialize(settings.Progress);
        var pendingEgg = settings.PendingEgg;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, true));
            Invoke(window, "BuildGenTabs");
            window.FindControl<CheckBox>("ShinyDex")!.IsChecked = true;
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 151);
            Invoke(window, "RefreshEvolutionUi");

            // Observe the first committed file during its color reveal, rather than requiring
            // an idle gap between automatically chained steps. Each step retains its own
            // five-second deadline; the two real animations together take about 5.6 seconds.
            Settings? firstCommitted = null;
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            effect.PropertyChanged += (_, e) =>
            {
                if (e.Property == EvolutionEffect.ProgressProperty && settings.SelectedDex == 5 && firstCommitted is null)
                    firstCommitted = Settings.LoadFrom(path);
            };
            Click(window.FindControl<Button>("EvolutionNotice")!);
            await EventuallyAsync(window, () => firstCommitted is not null);
            Assert.Equal(5, firstCommitted!.SelectedDex);
            Assert.True(firstCommitted.SelectedShiny);
            Assert.True(firstCommitted.IsOwned(5, true));
            Assert.False(firstCommitted.IsOwned(6, true));
            Assert.Equal(new[] { 4, 5 }, firstCommitted.For(4, true).History);
            Assert.Same(firstCommitted.For(4, true), firstCommitted.For(5, true));
            Assert.Null(firstCommitted.For(5, true).PendingEvolution);
            Assert.Equal(normalProgress, JsonSerializer.Serialize(firstCommitted.Progress));

            // No second click or form selection: the controller continues the same level-36 run.
            await EventuallyAsync(window, () => settings.SelectedDex == 6 && !Field<bool>(window, "_evolving"));

            Assert.True(settings.SelectedShiny);
            Assert.Equal("★ Lv. 36", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal(normalOwned, settings.Owned.Order().ToArray());
            Assert.Equal(normalProgress, JsonSerializer.Serialize(settings.Progress));
            Assert.Equal(new[] { 4, 5, 6 }, settings.ShinyOwned.Order().ToArray());
            foreach (var dex in new[] { 4, 5, 6 })
                Assert.Equal((36, 71), (settings.For(dex, true).Level, settings.For(dex, true).Exp));
            Assert.Same(settings.For(4, true), settings.For(6, true));
            Assert.Equal(new[] { 4, 5, 6 }, settings.For(4, true).History);

            foreach (var dex in new[] { 5, 6 })
            {
                Assert.Contains(assets.Requests, request => request.EndsWith($"/pokemon/exp/shiny/{dex}.json"));
                Assert.Contains(assets.Requests, request => request.EndsWith($"/pokemon/exp/shiny/{dex}.png"));
                Assert.DoesNotContain(assets.Requests, request => request.Contains("/pokemon/") &&
                    request.Contains($"/{dex}.") && !request.Contains("/shiny/"));
            }

            // Evolve already saved each step; do not call Save here and conceal a persistence omission.
            var restored = Settings.LoadFrom(path)!;
            Assert.Equal(6, restored.SelectedDex);
            Assert.True(restored.SelectedShiny);
            Assert.Equal(normalOwned, restored.Owned.Order().ToArray());
            Assert.Equal(normalProgress, JsonSerializer.Serialize(restored.Progress));
            Assert.Equal(new[] { 4, 5, 6 }, restored.ShinyOwned.Order().ToArray());
            foreach (var dex in new[] { 4, 5, 6 })
                Assert.Equal((36, 71), (restored.For(dex, true).Level, restored.For(dex, true).Exp));
            Assert.Same(restored.For(4, true), restored.For(6, true));
            Assert.Null(restored.For(6, true).PendingEvolution);
            Assert.Equal(pendingEgg, restored.PendingEgg);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }
}
