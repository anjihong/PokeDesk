using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task DefaultDexShowsBothOwnedColorsAndShinyOnlyIsAnOptionalFilter()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.Owned.Add(7);
        settings.ShinyOwned.UnionWith([4, 25]);
        settings.For(4).Level = 3;
        settings.For(4).Exp = 9;
        settings.For(4, true).Level = 8;
        settings.For(4, true).Exp = 99;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 153);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            var cells = grid.Children.OfType<RadioButton>().ToArray();
            var normal = cells.Single(cell => Choice(cell) == (4, false));
            var shiny = cells.Single(cell => Choice(cell) == (4, true));
            Assert.Equal(Array.IndexOf(cells, normal) + 1, Array.IndexOf(cells, shiny));
            Assert.Contains("shiny", shiny.Classes);
            Assert.DoesNotContain("shiny", normal.Classes);
            Assert.NotSame(Assert.IsType<Image>(normal.Content).Source, Assert.IsType<Image>(shiny.Content).Source);
            Assert.False(cells.Single(cell => Choice(cell) == (25, false)).IsEnabled);
            Assert.True(cells.Single(cell => Choice(cell) == (25, true)).IsEnabled);
            Assert.Equal("보유 4/2072", window.FindControl<TextBlock>("OwnedCount")!.Text);

            shiny.IsChecked = true; // No filter toggle is needed to select an owned shiny.
            await EventuallyAsync(window, () => settings.SelectedDex == 4 && settings.SelectedShiny);
            Assert.Equal("파이리 ★", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.Equal("Lv.8 · 경험치 99/240", TipText(window.FindControl<Grid>("DexDetailHeader")!));
            Invoke(window, "AddExp");
            Assert.Equal(100, settings.For(4, true).Exp);
            Assert.Equal((3, 9), (settings.For(4).Level, settings.For(4).Exp));

            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            Assert.Equal(new[] { (4, false), (4, true), (7, false), (25, true) }, grid.Children.OfType<RadioButton>().Select(Choice));
            var shinyFilter = window.FindControl<CheckBox>("ShinyDex")!;
            shinyFilter.IsChecked = true;
            await WaitForDexAsync(window, 2);
            Assert.Equal(new[] { (4, true), (25, true) }, grid.Children.OfType<RadioButton>().Select(Choice));
            Assert.Equal("보유 2/1036", window.FindControl<TextBlock>("OwnedCount")!.Text);
            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = false;
            Assert.Equal(151, grid.Children.Count);
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.True(Choice(cell).Shiny));
            shinyFilter.IsChecked = false;
            await WaitForDexAsync(window, 153);
            Assert.True(settings.SelectedShiny);
            Assert.Equal("파이리 ★", window.FindControl<TextBlock>("DexDetailName")!.Text);

            grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (7, false)).IsChecked = true;
            await EventuallyAsync(window, () => settings.SelectedDex == 7 && !settings.SelectedShiny);
            shinyFilter.IsChecked = true;
            await WaitForDexAsync(window, 151);
            Assert.Equal("이로치 포켓몬을 선택하세요", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.Equal(7, settings.SelectedDex);
            shinyFilter.IsChecked = false;
            await WaitForDexAsync(window, 153);
            Assert.Equal("꼬부기", window.FindControl<TextBlock>("DexDetailName")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ShinyHatchAppearsImmediatelyInTheMixedOwnedList()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new PendingEgg(EggKind.Shiny, 7, true);
        settings.Eggs = 1;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 151);
            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            await InvokeAsync(window, "HatchAsync");
            await EventuallyAsync(window, () => Field<SpriteAtlas?>(window, "_resultAtlas") != null);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(new[] { (4, false), (7, true) }, grid.Children.OfType<RadioButton>().Select(Choice));
            Assert.NotNull(Assert.IsType<Image>(((RadioButton)grid.Children[1]).Content).Source);
            Assert.Equal("보유 2/2072", window.FindControl<TextBlock>("OwnedCount")!.Text);
            Assert.False(window.FindControl<CheckBox>("ShinyDex")!.IsChecked == true);
            Assert.Equal(4, settings.SelectedDex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ShinyEvolutionAddsItsNewFormToTheMixedOwnedList()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.ShinyOwned.Add(4);
        settings.For(4, true).Level = 16;
        settings.SelectedShiny = true;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, true));
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 152);
            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            await InvokeAsync(window, "CheckEvolutionAsync");
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(new[] { (4, false), (4, true), (5, true) }, grid.Children.OfType<RadioButton>().Select(Choice));
            Assert.Equal(5, settings.SelectedDex);
            Assert.True(settings.SelectedShiny);
            Assert.Equal("리자드 ★", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.Equal("보유 3/2072", window.FindControl<TextBlock>("OwnedCount")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LateIconLoadCannotRestoreAnOldGenerationOrColorFilter()
    {
        using var assets = new UiAssets();
        var originalHttp = SpriteAtlas.Http;
        var delayed = new MixedDexDelayedIcons(originalHttp);
        using var client = new HttpClient(delayed);
        SpriteAtlas.Http = client;
        var settings = Settings.NewPreview(4);
        settings.ShinyOwned.UnionWith([4, 172]);
        var window = new MainWindow(settings, false);
        Task? staleRequest = null;
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            staleRequest = InvokeAsync(window, "RefreshDexAsync");
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var shinyFilter = window.FindControl<CheckBox>("ShinyDex")!;
            shinyFilter.IsChecked = true;
            Invoke(window, "SelectGenTab", 2);
            shinyFilter.IsChecked = false;
            await WaitForDexAsync(window, 101);

            delayed.Release.TrySetResult();
            await staleRequest;
            Dispatcher.UIThread.RunJobs();
            var choices = window.FindControl<WrapPanel>("IconGrid")!.Children.OfType<RadioButton>().Select(Choice).ToArray();
            Assert.Equal(101, choices.Length);
            Assert.All(choices, choice => Assert.InRange(choice.Dex, 152, 251));
            Assert.Equal(new[] { (172, true) }, choices.Where(choice => choice.Shiny));
            Assert.Equal(2, Field<int>(window, "_visibleGeneration"));
            Assert.False(shinyFilter.IsChecked == true);
        }
        finally
        {
            delayed.Release.TrySetResult();
            try { if (staleRequest != null) await staleRequest; }
            finally
            {
                window.Close();
                SpriteAtlas.Http = originalHttp;
            }
        }
    }

    private sealed class MixedDexDelayedIcons(HttpClient inner) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/pokemon_icons_1.json"))
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            // The inner client is UiAssets' deterministic fake, never a real network client.
            return await inner.GetAsync(request.RequestUri, cancellationToken);
        }
    }
}
