using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task RegionalFormsRemainInTheirGenerationsAndMixedColorFilters()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(4052);
        settings.AddOwned(8194);
        settings.AddOwned(4052, true);
        settings.SelectedDex = 4052;
        settings.For(4052).Level = 7;
        settings.For(4052).Exp = 13;
        settings.For(4052, true).Level = 9;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 8);
            await WaitForDexAsync(window, 107); // 96 national species + 10 forms + one owned shiny.
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            var normal = RegionalCell(grid, 4052, false);
            var shiny = RegionalCell(grid, 4052, true);
            Assert.Equal(grid.Children.IndexOf(normal) + 1, grid.Children.IndexOf(shiny));
            Assert.True(normal.IsEnabled);
            Assert.True(shiny.IsEnabled);
            Assert.Contains("#52 가라르 나옹", TipText(normal));
            Assert.Contains("Lv.7", TipText(normal));
            Assert.Contains("#52 가라르 나옹 ★ 이로치", TipText(shiny));
            Assert.Contains("Lv.9", TipText(shiny));
            Assert.DoesNotContain("#4052", TipText(normal));
            Assert.Equal("보유 4/2072", window.FindControl<TextBlock>("OwnedCount")!.Text);
            Assert.Equal("가라르 나옹", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.Equal("No.0052", window.FindControl<TextBlock>("DexDetailNumber")!.Text);
            Assert.Equal("강철", window.FindControl<TextBlock>("DexDetailTypes")!.Text);
            Assert.Equal("Lv.7 · 경험치 13/210", TipText(window.FindControl<Grid>("DexDetailHeader")!));
            var unowned = RegionalCell(grid, 4083, false);
            Assert.False(unowned.IsEnabled);
            Assert.Contains("#83 ???", TipText(unowned));
            Assert.DoesNotContain("파오리", TipText(unowned));

            var shinyOnly = window.FindControl<CheckBox>("ShinyDex")!;
            var ownedOnly = window.FindControl<CheckBox>("OwnedOnly")!;
            shinyOnly.IsChecked = true;
            await WaitForDexAsync(window, 106);
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.True(Choice(cell).Shiny));
            Assert.Equal("보유 1/1036", window.FindControl<TextBlock>("OwnedCount")!.Text);
            ownedOnly.IsChecked = true;
            Assert.Equal((4052, true), Choice(Assert.IsType<RadioButton>(Assert.Single(grid.Children))));
            shinyOnly.IsChecked = false;
            await WaitForDexAsync(window, 2);
            Assert.Equal(new[] { (4052, false), (4052, true) }, grid.Children.OfType<RadioButton>().Select(Choice));

            Invoke(window, "SelectGenTab", 9);
            await WaitForDexAsync(window, 1);
            var paldea = Assert.IsType<RadioButton>(Assert.Single(grid.Children));
            Assert.Equal((8194, false), Choice(paldea));
            Assert.Contains("#194 팔데아 우파", TipText(paldea));
            Invoke(window, "SelectGenTab", 0);
            await WaitForDexAsync(window, 4);
            ownedOnly.IsChecked = false;
            Assert.Equal(1037, grid.Children.Count);
            Assert.Equal(Enumerable.Range(1, 9).SelectMany(PokemonIcons.Entries),
                grid.Children.OfType<RadioButton>().Select(Choice).Where(choice => !choice.Shiny).Select(choice => choice.Dex));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RegionalAndEvolvedIconLevelsReadTheSharedGrowthRunWithoutCreatingUnownedRecords()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(4052);
        settings.For(4052).Level = 50;
        settings.For(4052).Exp = 17;
        settings.PrepareEvolution(4052, target: 863);
        settings.CompleteEvolution(4052, false, 863);
        settings.AddOwned(4052, true);
        settings.For(4052, true).Level = 9;
        settings.SelectedDex = 4052; // Viewing a previous appearance keeps the same growth run.
        var normalRecords = settings.Progress.Count;
        var shinyRecords = settings.ShinyProgress.Count;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 8);
            await WaitForDexAsync(window, 107);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Same(settings.For(4052), settings.For(863));
            Assert.Contains("Lv.50", TipText(RegionalCell(grid, 4052, false)));
            Assert.Contains("Lv.50", TipText(RegionalCell(grid, 863, false)));
            Assert.Contains("Lv.9", TipText(RegionalCell(grid, 4052, true)));
            Assert.Equal("Lv.50 · 경험치 17/1500", TipText(window.FindControl<Grid>("DexDetailHeader")!));
            Assert.Equal(normalRecords, settings.Progress.Count);
            Assert.Equal(shinyRecords, settings.ShinyProgress.Count);
        }
        finally { window.Close(); }
    }

    private static RadioButton RegionalCell(WrapPanel grid, int dex, bool shiny) =>
        grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (dex, shiny));
}
