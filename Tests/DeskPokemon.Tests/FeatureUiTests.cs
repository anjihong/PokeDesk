using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WholeDexIncludesAllSpeciesAndShowsKoreanMetadataAndEmptyFilters(int scale)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var window = new MainWindow(settings, false);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            Invoke(window, "BuildGenTabs");
            var generations = window.FindControl<StackPanel>("GenTabs")!;
            Assert.Equal(10, generations.Children.Count);
            Assert.Equal("전체", ((RadioButton)generations.Children[0]).Content);
            ((RadioButton)generations.Children[0]).IsChecked = true;
            await WaitForDexAsync(window, 1036);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(Enumerable.Range(1, 9).SelectMany(PokemonIcons.Entries),
                grid.Children.OfType<RadioButton>().Select(cell => Choice(cell).Dex));
            Assert.All(grid.Children.OfType<RadioButton>(), cell =>
            {
                var image = Assert.IsType<Image>(cell.Content);
                Assert.Equal(40, image.Width);
                Assert.Equal(40, image.Height);
                Assert.NotNull(image.Source);
            });
            Assert.Equal("파이리", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.Equal("No.0004", window.FindControl<TextBlock>("DexDetailNumber")!.Text);
            Assert.Equal("불꽃", window.FindControl<TextBlock>("DexDetailTypes")!.Text);
            var description = window.FindControl<TextBlock>("DexDetailDescription")!.Text!;
            Assert.Equal("Lv.1 · 경험치 0/30", TipText(window.FindControl<Grid>("DexDetailHeader")!));
            Assert.Equal(PokemonDetails.For(4).Description, description);
            Assert.Matches("[가-힣]", description);
            Assert.NotNull(window.FindControl<Image>("DexDetailImage")!.Source);
            var hidden = grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (151, false));
            Assert.False(hidden.IsEnabled);
            Assert.DoesNotContain("뮤", TipText(hidden));
            hidden.IsChecked = true; // Defensive selection path cannot reveal an unowned pet.
            Assert.Equal(4, settings.SelectedDex);
            Assert.Equal("파이리", window.FindControl<TextBlock>("DexDetailName")!.Text);
            Assert.False(window.FindControl<Button>("RetryDexButton")!.IsVisible);

            await OpenPanelAsync(window, "dex");
            var firstRow = grid.Children.OfType<RadioButton>().Take(6).ToArray();
            Assert.All(firstRow, cell => Assert.Equal(firstRow[0].Bounds.Y, cell.Bounds.Y));
            Assert.True(((RadioButton)grid.Children[6]).Bounds.Y > firstRow[0].Bounds.Y);
            Capture(window, "main-whole-dex", scale);

            var owned = window.FindControl<CheckBox>("OwnedOnly")!;
            owned.IsChecked = true;
            Assert.Single(grid.Children);
            window.FindControl<CheckBox>("ShinyDex")!.IsChecked = true;
            await WaitForDexAsync(window, 0);
            Assert.Contains("이로치", window.FindControl<TextBlock>("DexStatusText")!.Text);
            Assert.True(window.FindControl<TextBlock>("DexStatusText")!.IsVisible);
            Assert.Equal("보유 0/1036", window.FindControl<TextBlock>("OwnedCount")!.Text);
            Assert.Null(window.FindControl<Image>("DexDetailImage")!.Source);
            Assert.Equal(4, settings.SelectedDex);
            Assert.False(settings.SelectedShiny);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MissingGenerationKeepsEverySpeciesAndRetryFillsItsPlaceholders()
    {
        using var assets = new UiAssets { MissingIconGeneration = 2 };
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            var generations = window.FindControl<StackPanel>("GenTabs")!;
            generations.Children.OfType<RadioButton>().Single(tab => (int)tab.Tag! == 2).IsChecked = true;
            await WaitForDexAsync(window, 100);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(Enumerable.Range(152, 100), grid.Children.OfType<RadioButton>().Select(Choice).Select(choice => choice.Dex));
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.IsType<TextBlock>(cell.Content));
            var retry = window.FindControl<Button>("RetryDexButton")!;
            Assert.True(retry.IsVisible);
            Assert.Contains("다시 시도", window.FindControl<TextBlock>("DexStatusText")!.Text);
            assets.MissingIconGeneration = 0;
            Click(retry);
            await WaitForDexAsync(window, 100);
            Assert.False(retry.IsVisible);
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.IsType<Image>(cell.Content));
            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            Assert.Empty(grid.Children);
            Assert.Contains("보유한 포켓몬이 없습니다", window.FindControl<TextBlock>("DexStatusText")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SettingsControlsFlipOnlyThePetScaleWithinTheScreenAndRegisterOnlyOnRequest(int scale)
    {
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var startup = new FakeStartupRegistration();
        var window = new MainWindow(settings, false, startup);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            Assert.Empty(startup.Requests);
            Assert.True(startup.Queries >= 1);
            await OpenPanelAsync(window, "settings");
            Assert.True(window.FindControl<StackPanel>("SettingsPanel")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("DexPanel")!.IsVisible);
            Assert.Empty(startup.Requests); // Opening/querying settings must not install anything.
            var flip = window.FindControl<CheckBox>("FlipCheckBox")!;
            flip.IsChecked = true;
            Assert.True(settings.FlipHorizontal);
            Assert.Equal(-1, Assert.IsType<ScaleTransform>(window.FindControl<Border>("PetFacing")!.RenderTransform).ScaleX);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(window.FindControl<Canvas>("Stage")!.RenderTransform).ScaleX);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(window.FindControl<TextBlock>("LevelText")!.RenderTransform).ScaleX);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(Assert.IsType<TransformGroup>(window.FindControl<Canvas>("EggStage")!.RenderTransform).Children[0]).ScaleX);

            var up = window.FindControl<Button>("ScaleUpButton")!;
            var down = window.FindControl<Button>("ScaleDownButton")!;
            Assert.Equal(2, settings.UiScale);
            Assert.False(down.IsEnabled);
            foreach (var requested in new[] { 4, 6, 8 })
            {
                Click(up);
                await EventuallyAsync(window, () => settings.UiScale == requested && Math.Abs(window.Bounds.Width -
                    352 * ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!.LayoutTransform!).ScaleX) < .001);
                AssertPresentationFits(window, requested);
            }
            Assert.False(up.IsEnabled);
            foreach (var requested in new[] { 6, 4, 2 })
            {
                Click(down);
                await EventuallyAsync(window, () => settings.UiScale == requested && Math.Abs(window.Bounds.Width -
                    352 * ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!.LayoutTransform!).ScaleX) < .001);
                AssertPresentationFits(window, requested);
            }
            Assert.False(down.IsEnabled);
            Assert.True(up.IsEnabled);

            var checkbox = window.FindControl<CheckBox>("StartupCheckBox")!;
            checkbox.IsChecked = true;
            Assert.Equal(new[] { true }, startup.Requests);
            Assert.True(checkbox.IsChecked);
            Assert.Contains("다음 로그인", window.FindControl<TextBlock>("StartupStatus")!.Text);
            Capture(window, "settings", scale);
            checkbox.IsChecked = false;
            Assert.Equal(new[] { true, false }, startup.Requests);
            flip.IsChecked = false;
            Assert.False(settings.FlipHorizontal);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(window.FindControl<Border>("PetFacing")!.RenderTransform).ScaleX);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task StartupRegistrationFailureRestoresTheCheckboxWithoutRetryingItself()
    {
        var startup = new FakeStartupRegistration { Failure = "테스트 등록 실패" };
        var window = new MainWindow(Settings.NewPreview(4), false, startup);
        try
        {
            ShowAndLayout(window);
            await OpenPanelAsync(window, "settings");
            var checkbox = window.FindControl<CheckBox>("StartupCheckBox")!;
            checkbox.IsChecked = true;
            Assert.False(checkbox.IsChecked);
            Assert.Equal(new[] { true }, startup.Requests);
            Assert.Contains("테스트 등록 실패", window.FindControl<TextBlock>("StartupStatus")!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelUpEvolvesOnlyTheSelectedColorAndEachAppearanceSharesItsGrowthRun(bool shiny)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(4, true);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 15;
        settings.For(4, shiny).Exp = Settings.ExpToNext(15) - 1;
        settings.For(4, !shiny).Level = 3;
        settings.For(4, !shiny).Exp = 9;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, shiny));
            Invoke(window, "BuildGenTabs");
            window.FindControl<CheckBox>("ShinyDex")!.IsChecked = shiny;
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, shiny ? 151 : 152);
            Invoke(window, "AddExp"); // The input path triggers the real asynchronous evolution controller.
            await EventuallyAsync(window, () => settings.SelectedDex == 5 && !Field<bool>(window, "_evolving"));
            Assert.Equal(shiny, settings.SelectedShiny);
            Assert.True(settings.IsOwned(4, shiny));
            Assert.True(settings.IsOwned(5, shiny));
            Assert.False(settings.IsOwned(5, !shiny));
            Assert.Equal(16, settings.For(4, shiny).Level);
            Assert.Equal(0, settings.For(4, shiny).Exp);
            Assert.Equal(16, settings.For(5, shiny).Level);
            Assert.Same(settings.For(4, shiny), settings.For(5, shiny));
            Assert.Equal(3, settings.For(4, !shiny).Level);
            Assert.Equal(9, settings.For(4, !shiny).Exp);
            Assert.Equal(1, window.FindControl<Canvas>("Stage")!.Opacity);

            settings.For(4, shiny).Level = 36;
            settings.For(4, shiny).Exp = 17;
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (4, shiny)).IsChecked = true;
            await EventuallyAsync(window, () => settings.SelectedDex == 4 && !Field<bool>(window, "_evolving"));
            grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (5, shiny)).IsChecked = true;
            await EventuallyAsync(window, () => settings.SelectedDex == 6 && !Field<bool>(window, "_evolving"));
            Assert.True(settings.IsOwned(4, shiny));
            Assert.True(settings.IsOwned(5, shiny));
            Assert.True(settings.IsOwned(6, shiny));
            Assert.False(settings.IsOwned(6, !shiny));
            Assert.Same(settings.For(4, shiny), settings.For(5, shiny));
            Assert.Same(settings.For(5, shiny), settings.For(6, shiny));
            Assert.All(new[] { 4, 5, 6 }, dex =>
                Assert.Equal((36, 17), (settings.For(dex, shiny).Level, settings.For(dex, shiny).Exp)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BranchEvolutionPreparesOneRandomUnownedResultAndPreservesTheOtherBranch(int scale)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(265);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        settings.For(265).Level = 7;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 265, false));
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 3);
            await WaitForDexAsync(window, 135);
            var cell = window.FindControl<WrapPanel>("IconGrid")!.Children.OfType<RadioButton>().Single(item => Choice(item) == (265, false));
            Assert.Contains("진화 가능", TipText(cell));
            Assert.Contains("evolvable", cell.Classes);
            var notice = window.FindControl<Button>("EvolutionNotice")!;
            Assert.True(notice.IsVisible);
            var choices = window.FindControl<WrapPanel>("EvolutionChoices")!;
            Assert.False(choices.IsVisible);
            Assert.Empty(choices.Children);
            Assert.Equal(265, settings.SelectedDex);
            Assert.False(settings.IsOwned(266));
            Assert.False(settings.IsOwned(268));
            Assert.Null(settings.For(265).PendingEvolution);
            // Capture deterministic readiness before PrepareEvolution draws its random result.
            await OpenPanelAsync(window, "dex");
            Capture(window, "evolution-ready", scale);
            Click(notice);
            Assert.False(choices.IsVisible);
            Assert.Empty(choices.Children);
            await EventuallyAsync(window, () => settings.SelectedDex is 266 or 268 && !Field<bool>(window, "_evolving"));
            var evolved = settings.SelectedDex;
            var otherBranch = evolved == 266 ? 268 : 266;
            Assert.True(settings.IsOwned(265));
            Assert.True(settings.IsOwned(evolved));
            Assert.False(settings.IsOwned(otherBranch));
            Assert.Same(settings.For(265), settings.For(evolved));
            Assert.Equal(new[] { 265, evolved }, settings.For(265).History);
            Assert.Null(settings.For(265).PendingEvolution);
            Assert.False(choices.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EvolutionImageFailurePreservesOwnershipSelectionAndAllowsRetry()
    {
        using var assets = new UiAssets { MissingSpriteDex = 5 };
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var before = window.FindControl<Image>("Sprite")!.Source;
            var notice = window.FindControl<Button>("EvolutionNotice")!;
            Click(notice);
            await EventuallyAsync(window, () => window.FindControl<TextBlock>("SpriteStatus")!.IsVisible && !Field<bool>(window, "_evolving"));
            Assert.Equal(4, settings.SelectedDex);
            Assert.Same(before, window.FindControl<Image>("Sprite")!.Source);
            Assert.True(settings.IsOwned(4));
            Assert.False(settings.IsOwned(5));
            Assert.Equal(16, settings.For(4).Level);
            Assert.Equal(1, window.FindControl<Canvas>("Stage")!.Opacity);
            Assert.True(notice.IsEnabled);
            assets.MissingSpriteDex = 0;
            Click(notice);
            await EventuallyAsync(window, () => settings.SelectedDex == 5 && !Field<bool>(window, "_evolving"));
            Assert.True(settings.IsOwned(5));
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task WaitForDexAsync(MainWindow window, int count) => EventuallyAsync(window,
        () => !Field<bool>(window, "_dexLoading") && window.FindControl<WrapPanel>("IconGrid")!.Children.Count == count);

    private static async Task OpenPanelAsync(MainWindow window, string tag)
    {
        var tab = window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>().Single(tab => Equals(tab.Tag, tag));
        tab.IsChecked = true;
        var drawer = window.FindControl<Border>("Drawer")!;
        var content = window.FindControl<Border>("DrawerContent")!;
        var root = window.FindControl<StackPanel>("Root")!;
        var zoom = window.FindControl<LayoutTransformControl>("UiZoom")!;
        (double Target, Rect Window, Rect Root, Rect Zoom, Rect Drawer, Rect Content)? previous = null;
        var stableSamples = 0;
        await EventuallyAsync(window, () =>
        {
            // DesiredSize is constrained to the current drawer during animation; equality
            // with it can capture a partly opened panel. Wait for its independent target
            // and the actual SizeToContent window, then require consecutive settled layouts.
            var target = Field<double>(window, "_drawerTargetHeight");
            var ready = tab.IsChecked == true && target > 0 && !Field<bool>(window, "_drawerRemeasurePending") &&
                Math.Abs(drawer.Height - target) < .001 && Math.Abs(drawer.Bounds.Height - target) < .001 &&
                Math.Abs(content.Bounds.Height - target) < .001 &&
                window.Bounds.Width > 0 && window.Bounds.Height > 0 &&
                Math.Abs(window.Bounds.Width - zoom.Bounds.Width) < .001 &&
                Math.Abs(window.Bounds.Height - zoom.Bounds.Height) < .001;
            if (!ready)
            {
                previous = null;
                stableSamples = 0;
                return false;
            }
            var current = (target, window.Bounds, root.Bounds, zoom.Bounds, drawer.Bounds, content.Bounds);
            stableSamples = previous == current ? stableSamples + 1 : 1;
            previous = current;
            return stableSamples >= 3;
        });
    }

    private static void AssertPresentationFits(MainWindow window, int requested)
    {
        var zoom = Assert.IsType<ScaleTransform>(window.FindControl<LayoutTransformControl>("UiZoom")!.LayoutTransform);
        Assert.InRange(zoom.ScaleX, .25, requested / 2d);
        Assert.Equal(zoom.ScaleX, zoom.ScaleY);
        Assert.Equal(352 * zoom.ScaleX, window.Bounds.Width, 3);
        Assert.StartsWith(requested + "배", window.FindControl<TextBlock>("ScaleValue")!.Text);
        var area = (PixelRect)typeof(MainWindow).GetProperty("WorkingArea", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        Assert.True(window.Bounds.Width * window.DesktopScaling <= area.Width);
        Assert.True(window.Bounds.Height * window.DesktopScaling <= area.Height);
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public List<bool> Requests { get; } = new();
        public int Queries { get; private set; }
        public bool Enabled { get; private set; }
        public string? Failure { get; init; }
        public StartupRegistrationResult Query()
        {
            Queries++;
            return new(true, Enabled);
        }
        public StartupRegistrationResult SetEnabled(bool enabled)
        {
            Requests.Add(enabled);
            if (Failure != null) return new(true, Enabled, Failure);
            Enabled = enabled;
            return new(true, Enabled);
        }
    }
}
