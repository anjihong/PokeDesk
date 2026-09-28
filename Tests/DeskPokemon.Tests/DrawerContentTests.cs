using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task GrowingStartupErrorKeepsTheSettingsReachableAndRestoresTheDrawerOrigin()
    {
        var startup = new FakeStartupRegistration { Failure = string.Join("\n", Enumerable.Repeat("등록 위치를 확인할 수 없습니다. 다시 확인해 주세요.", 24)) };
        var window = new MainWindow(Settings.NewPreview(4), false, startup);
        try
        {
            var (_, drawer, area, _) = PreparePositionedDrawer(window);
            var origin = new PixelPoint(area.X + 40, area.Y + 20);
            window.Position = origin;
            var collapsedHeight = window.Bounds.Height;
            await OpenPanelAsync(window, "settings");
            var before = drawer.Height;

            window.FindControl<CheckBox>("StartupCheckBox")!.IsChecked = true;
            await EventuallyAsync(window, () => drawer.Height > before + 30 &&
                Math.Abs(drawer.Height - Field<double>(window, "_drawerTargetHeight")) < .001);

            var scroll = window.FindControl<ScrollViewer>("DrawerScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var exit = window.FindControl<StackPanel>("SettingsPanel")!.Children.OfType<Button>()
                .Single(button => Equals(button.Content, "DeskPokemon 종료"));
            var visibleDrawer = BoundsIn(window, drawer);
            var visibleExit = BoundsIn(window, exit);
            Assert.True(visibleExit.Top >= visibleDrawer.Top);
            Assert.True(visibleExit.Bottom <= visibleDrawer.Bottom + 1);
            Assert.True(window.Bounds.Height * window.DesktopScaling <= area.Height);

            SettingsTab(window).IsChecked = false;
            await EventuallyAsync(window, () => drawer.Height == 0);
            Assert.Equal(origin, window.Position);
            Assert.Equal(collapsedHeight, window.Bounds.Height, 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChangingContentDuringDrawerAnimationKeepsTheAnchorAndDoesNotReopenOnClose()
    {
        var startup = new FakeStartupRegistration { Failure = string.Join("\n", Enumerable.Repeat("자동 시작 등록에 실패했습니다.", 18)) };
        var window = new MainWindow(Settings.NewPreview(4), false, startup);
        try
        {
            var (_, drawer, area, _) = PreparePositionedDrawer(window);
            var origin = new PixelPoint(area.X + 40, area.Y + 25);
            window.Position = origin;
            var tab = SettingsTab(window);
            tab.IsChecked = true;
            SeekDrawerFrameWithoutClock(window, .07);
            window.UpdateLayout();
            var partialHeight = drawer.Height;
            var oldTarget = Field<double>(window, "_drawerTargetHeight");
            Assert.InRange(partialHeight, 1, oldTarget - 1);

            window.FindControl<CheckBox>("StartupCheckBox")!.IsChecked = true;
            await EventuallyAsync(window, () => Field<double>(window, "_drawerTargetHeight") > oldTarget);
            Assert.InRange(drawer.Height, partialHeight, Field<double>(window, "_drawerTargetHeight") - .01);
            Assert.Equal(origin, Field<PixelPoint?>(window, "_positionBeforeDrawer"));

            tab.IsChecked = false;
            window.FindControl<TextBlock>("StartupStatus")!.Text += "\n" + new string('가', 240);
            await EventuallyAsync(window, () => drawer.Height == 0);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, drawer.Height);
            Assert.Equal(0, Field<double>(window, "_drawerTargetHeight"));
            Assert.Equal(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RetryDuringEvolutionCannotReloadThePreviousSpecies()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var previous = window.FindControl<Image>("Sprite")!.Source;
            var evolution = InvokeAsync(window, "EvolveAsync", 5);
            Assert.True(Field<bool>(window, "_evolving"));
            var request = Field<int>(window, "_loadRequest");

            Invoke(window, "OnRetrySprite", window, new RoutedEventArgs());
            Assert.Equal(request, Field<int>(window, "_loadRequest"));
            Assert.Same(previous, window.FindControl<Image>("Sprite")!.Source);
            await evolution;

            Assert.Equal(5, settings.SelectedDex);
            Assert.True(settings.IsOwned(5));
            Assert.NotSame(previous, window.FindControl<Image>("Sprite")!.Source);
            Assert.Equal("리자드", window.FindControl<TextBlock>("PetName")!.Text);
            Assert.False(Field<bool>(window, "_evolving"));
        }
        finally { window.Close(); }
    }

    private static ToggleButton SettingsTab(MainWindow window) =>
        window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>().Single(tab => Equals(tab.Tag, "settings"));
}
