using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task ThreeTabsKeepBoxOwnedOnlyAndRestoreTheDexFilterWithSharedShinySelection()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(7);
        settings.AddOwned(4, true);
        settings.AddOwned(25, true);
        settings.For(4).Level = 3;
        settings.For(4, true).Level = 7;
        var startup = new FakeStartupRegistration();
        var window = new MainWindow(settings, false, startup);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 153);
            var tabs = window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>().ToArray();
            Assert.Equal(new[] { "dex", "box", "settings" }, tabs.Select(tab => (string)tab.Tag!));
            var dex = ReferenceMenuTab(window, "dex");
            var box = ReferenceMenuTab(window, "box");
            var preferences = ReferenceMenuTab(window, "settings");
            var owned = window.FindControl<CheckBox>("OwnedOnly")!;
            var shiny = window.FindControl<CheckBox>("ShinyDex")!;
            var grid = window.FindControl<WrapPanel>("IconGrid")!;

            await ReferencePointerClickAsync(window, dex);
            Assert.True(dex.IsChecked);
            await OpenPanelAsync(window, "dex");
            Assert.True(owned.IsEnabled);
            Assert.False(owned.IsChecked == true);
            Assert.Equal(153, grid.Children.Count);

            await ReferencePointerClickAsync(window, box);
            Assert.True(box.IsChecked);
            await OpenPanelAsync(window, "box");
            Assert.Single(tabs, tab => tab.IsChecked == true);
            Assert.True(window.FindControl<StackPanel>("DexPanel")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("SettingsPanel")!.IsVisible);
            Assert.True(owned.IsChecked);
            Assert.False(owned.IsEnabled);
            Assert.Equal(new[] { (4, false), (4, true), (7, false), (25, true) },
                grid.Children.OfType<RadioButton>().Select(Choice));
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.True(cell.IsEnabled));
            await ReferencePointerClickAsync(window, owned); // A disabled filter cannot expose unowned entries in the box.
            Assert.True(owned.IsChecked);
            Assert.Equal(4, grid.Children.Count);

            var shinyCharmander = grid.Children.OfType<RadioButton>().Single(cell => Choice(cell) == (4, true));
            await ReferencePointerClickAsync(window, shinyCharmander);
            await EventuallyAsync(window, () => settings.SelectedShiny && Field<int>(window, "_pendingSelections") == 0);
            Assert.Equal(4, settings.SelectedDex);
            Assert.Equal("★ Lv. 7", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal(3, settings.For(4).Level);

            await ReferencePointerClickAsync(window, shiny);
            await WaitForDexAsync(window, 2);
            Assert.All(grid.Children.OfType<RadioButton>(), cell => Assert.True(Choice(cell).Shiny));

            ReferenceKeyboardActivate(preferences);
            Assert.True(preferences.IsChecked);
            await OpenPanelAsync(window, "settings");
            Assert.True(window.FindControl<StackPanel>("SettingsPanel")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("DexPanel")!.IsVisible);
            Assert.Empty(startup.Requests);

            ReferenceKeyboardActivate(box);
            Assert.True(box.IsChecked);
            await OpenPanelAsync(window, "box");
            Assert.True(shiny.IsChecked);
            Assert.True(owned.IsChecked);
            Assert.False(owned.IsEnabled);
            Assert.Equal(2, grid.Children.Count);

            ReferenceKeyboardActivate(dex);
            Assert.True(dex.IsChecked);
            await OpenPanelAsync(window, "dex");
            Assert.True(shiny.IsChecked);
            Assert.True(owned.IsEnabled);
            Assert.False(owned.IsChecked == true); // Restore the dex's previous preference, rather than the box's forced value.
            await WaitForDexAsync(window, 151);
            Assert.Equal("보유 2/1036", window.FindControl<TextBlock>("OwnedCount")!.Text);

            await ReferencePointerClickAsync(window, owned);
            await WaitForDexAsync(window, 2);
            await ReferencePointerClickAsync(window, box);
            Assert.True(box.IsChecked);
            await OpenPanelAsync(window, "box");
            await ReferencePointerClickAsync(window, shiny);
            await WaitForDexAsync(window, 4);
            await ReferencePointerClickAsync(window, dex);
            Assert.True(dex.IsChecked);
            await OpenPanelAsync(window, "dex");
            Assert.True(owned.IsChecked); // A checked dex preference is also preserved across the box.
            Assert.True(owned.IsEnabled);
            Assert.False(shiny.IsChecked == true);
            Assert.Equal(4, grid.Children.Count);
            Assert.Equal("보유 4/2072", window.FindControl<TextBlock>("OwnedCount")!.Text);
            Assert.Equal(1, (int)typeof(MainWindow).GetMethod("CheckedGen", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!);
            Assert.Equal(4, settings.SelectedDex);
            Assert.True(settings.SelectedShiny);

            ReferenceKeyboardActivate(dex);
            Assert.False(dex.IsChecked == true);
            await EventuallyAsync(window, () => window.FindControl<Border>("Drawer")!.Height == 0);
            Assert.DoesNotContain(tabs, tab => tab.IsChecked == true);
            Assert.Empty(startup.Requests);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GenerationArrowsAndFlyoutSupportPointerAndKeyboardAtEveryBoundary()
    {
        using var assets = new UiAssets();
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 151);
            await ReferencePointerClickAsync(window, ReferenceMenuTab(window, "dex"));
            Assert.True(ReferenceMenuTab(window, "dex").IsChecked);
            await OpenPanelAsync(window, "dex");
            var previous = window.FindControl<Button>("PreviousGenerationButton")!;
            var next = window.FindControl<Button>("NextGenerationButton")!;

            await ReferencePickGenerationAsync(window, 0, keyboard: false);
            await AssertGeneration(0);
            Assert.False(previous.IsEnabled);
            Assert.True(next.IsEnabled);
            await ReferencePointerClickAsync(window, previous);
            await AssertGeneration(0);

            ReferenceKeyboardActivate(next);
            await AssertGeneration(1);
            Assert.True(previous.IsEnabled);
            Assert.True(next.IsEnabled);
            await ReferencePointerClickAsync(window, previous);
            await AssertGeneration(0);

            await ReferencePickGenerationAsync(window, 9, keyboard: true);
            await AssertGeneration(9);
            Assert.True(previous.IsEnabled);
            Assert.False(next.IsEnabled);
            await ReferencePointerClickAsync(window, next);
            await AssertGeneration(9);
            Assert.Contains(window.FindControl<WrapPanel>("IconGrid")!.Children.OfType<RadioButton>(),
                cell => Choice(cell) == (8194, false)); // The last generation includes its regional appearance.

            ReferenceKeyboardActivate(previous);
            await AssertGeneration(8);
            Assert.True(next.IsEnabled);
            await ReferencePickGenerationAsync(window, 0, keyboard: false);
            await AssertGeneration(0);
            await ReferencePickGenerationAsync(window, 1, keyboard: true);
            await AssertGeneration(1);
            await ReferencePickGenerationAsync(window, 1, keyboard: false);
            await AssertGeneration(1); // Choosing the current value still dismisses the picker.

            async Task AssertGeneration(int generation)
            {
                var count = generation == 0 ? 1036 : PokemonIcons.Entries(generation).Count();
                await WaitForDexAsync(window, count);
                var selected = Assert.Single(window.FindControl<StackPanel>("GenTabs")!.Children.OfType<RadioButton>(),
                    tab => tab.IsChecked == true);
                Assert.Equal(generation, (int)selected.Tag!);
                Assert.Contains(generation == 0 ? "전체" : generation.ToString(),
                    window.FindControl<TextBlock>("GenerationText")!.Text);
                Assert.Equal(generation, Field<int>(window, "_visibleGeneration"));
            }
        }
        finally { window.Close(); }
    }

    private static ToggleButton ReferenceMenuTab(MainWindow window, string tag) =>
        window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>().Single(tab => Equals(tab.Tag, tag));

    [AvaloniaFact]
    public async Task ReferenceGridFitsFourRowsAndScrollsWithoutMovingTheToolbarOrTruncatingTheDescriptionData()
    {
        using var assets = new UiAssets();
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 151);
            await OpenPanelAsync(window, "dex");
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            var scroll = window.FindControl<ScrollViewer>("IconScroll")!;
            var picker = window.FindControl<Button>("GenerationPickerButton")!;
            var viewport = BoundsIn(window, scroll);
            Assert.True(BoundsIn(window, grid.Children[0]).Top >= viewport.Top + 2);
            Assert.True(BoundsIn(window, grid.Children[23]).Bottom <= viewport.Bottom);
            var toolbarPosition = BoundsIn(window, picker);
            var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(point, new Vector(0, -5), RawInputModifiers.None);
            await EventuallyAsync(window, () => scroll.Offset.Y > 0);
            Assert.Equal(toolbarPosition, BoundsIn(window, picker));
            window.MouseWheel(point, new Vector(0, 100), RawInputModifiers.None);
            await EventuallyAsync(window, () => scroll.Offset.Y == 0);
            var description = window.FindControl<TextBlock>("DexDetailDescription")!;
            Assert.Equal(2, description.MaxLines);
            Assert.Equal(PokemonDetails.For(4).Description, description.Text);
            Assert.Equal(description.Text, TipText(description));
        }
        finally { window.Close(); }
    }

    private static async Task ReferencePointerClickAsync(MainWindow window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var topLevel = TopLevel.GetTopLevel(control);
        Assert.NotNull(topLevel);
        topLevel.UpdateLayout();
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!.Value;
        topLevel.MouseMove(point);
        topLevel.MouseDown(point, MouseButton.Left);
        topLevel.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    private static void ReferenceKeyboardActivate(Control control)
    {
        var topLevel = TopLevel.GetTopLevel(control);
        Assert.NotNull(topLevel);
        Assert.True(control.Focus(NavigationMethod.Tab));
        topLevel.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        topLevel.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ReferencePickGenerationAsync(MainWindow window, int generation, bool keyboard)
    {
        var picker = window.FindControl<Button>("GenerationPickerButton")!;
        if (keyboard) ReferenceKeyboardActivate(picker);
        else await ReferencePointerClickAsync(window, picker);
        await EventuallyAsync(window, () => picker.Flyout?.IsOpen == true);
        var choices = window.FindControl<StackPanel>("GenTabs")!;
        Assert.Equal(Enumerable.Range(0, 10), choices.Children.OfType<RadioButton>().Select(tab => (int)tab.Tag!));
        var target = choices.Children.OfType<RadioButton>().Single(tab => Equals(tab.Tag, generation));
        await EventuallyAsync(window, () => TopLevel.GetTopLevel(target) != null && target.Bounds.Width > 0 && target.Bounds.Height > 0);
        if (keyboard) ReferenceKeyboardActivate(target);
        else await ReferencePointerClickAsync(window, target);
        await EventuallyAsync(window, () => target.IsChecked == true && picker.Flyout?.IsOpen == false);
    }
}
