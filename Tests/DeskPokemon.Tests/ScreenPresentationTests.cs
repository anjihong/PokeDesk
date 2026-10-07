using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task MovingToASmallerScreenRefitsWithoutASizeEventOrChangingTheSavedScale()
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = 8;
        var window = new MainWindow(settings, false);
        using var screens = new TestScreenLayout(window,
            new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true),
            new Screen(1, new PixelRect(2400, 0, 800, 700), new PixelRect(2400, 0, 800, 660), false));
        try
        {
            await PrepareScreenWindowAsync(window);
            var oldScale = ScreenUiScale(window);
            var oldSize = window.Bounds.Size;
            window.Position = new PixelPoint(2420, 30); // Native position notification; no explicit resize/refit.
            Assert.Equal(oldSize, window.Bounds.Size);
            await EventuallyAsync(window, () => ScreenUiScale(window) < oldScale && ScreenWindowFits(window));
            Assert.Equal(8, settings.UiScale);
            Assert.Equal(2400, window.Screens.ScreenFromWindow(window)!.WorkingArea.X);
            Assert.Contains("화면 맞춤", window.FindControl<TextBlock>("ScaleValue")!.Text);
            var settled = window.Position;
            var movements = 0;
            window.PositionChanged += (_, _) => movements++;
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Task.Delay(10);
            }
            Assert.Equal(settled, window.Position);
            Assert.Equal(0, movements); // Programmatic clamping must not create a position/refit feedback loop.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AWorkAreaChangeResizesTheOpenDrawerEvenWhenUiScaleDoesNotChange()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        var bounds = new PixelRect(0, 0, 1920, 1080);
        using var screens = new TestScreenLayout(window, new Screen(1, bounds, new PixelRect(0, 0, 1920, 1040), true));
        try
        {
            await PrepareScreenWindowAsync(window);
            var origin = window.Position;
            await OpenPanelAsync(window, "dex");
            var oldHeight = window.Bounds.Height;
            var oldScale = ScreenUiScale(window);
            screens.Change(new Screen(1, bounds, new PixelRect(0, 0, 1920, 520), true));
            await EventuallyAsync(window, () => window.Bounds.Height < oldHeight && ScreenWindowFits(window) &&
                !Field<bool>(window, "_screenRefitPending"));
            Assert.Equal(oldScale, ScreenUiScale(window));
            var dex = window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>()
                .Single(tab => Equals(tab.Tag, "dex"));
            dex.IsChecked = false;
            await WaitForDrawerCloseAsync(window);
            Assert.Equal(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DpiNotificationRefitsAnOpenDrawerAndItsNewCloseAnchor()
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = 8;
        var window = new MainWindow(settings, false);
        var area = new PixelRect(0, 0, 1600, 1000);
        using var screens = new TestScreenLayout(window, new Screen(1, area, area, true));
        try
        {
            await PrepareScreenWindowAsync(window);
            await OpenPanelAsync(window, "dex");
            var oldScale = ScreenUiScale(window);
            screens.SetDpi(1.5); // The headless platform raises the same ScalingChanged callback as Win32.
            await EventuallyAsync(window, () => ScreenUiScale(window) < oldScale && ScreenWindowFits(window));
            Assert.Equal(1.5, window.DesktopScaling);
            Assert.Equal(8, settings.UiScale);
            var dex = window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>()
                .Single(tab => Equals(tab.Tag, "dex"));
            dex.IsChecked = false;
            await WaitForDrawerCloseAsync(window);
            Assert.True(ScreenWindowFits(window));
            var origin = window.Position;
            await OpenPanelAsync(window, "dex");
            dex.IsChecked = false;
            await WaitForDrawerCloseAsync(window);
            Assert.Equal(origin, window.Position);
        }
        finally { window.Close(); }
    }

    private static async Task PrepareScreenWindowAsync(MainWindow window)
    {
        ShowAndLayout(window);
        typeof(MainWindow).GetField("_placed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        window.Position = new PixelPoint(100, 100);
        await EventuallyAsync(window, () => !Field<bool>(window, "_screenRefitPending") &&
            Math.Abs(window.Bounds.Width - 352 * ScreenUiScale(window)) < .001 && ScreenWindowFits(window));
    }

    private static double ScreenUiScale(MainWindow window) =>
        ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!.LayoutTransform!).ScaleX;

    private static bool ScreenWindowFits(MainWindow window)
    {
        var area = window.Screens.ScreenFromWindow(window)!.WorkingArea;
        return window.Position.X >= area.X && window.Position.Y >= area.Y &&
            window.Position.X + Math.Ceiling(window.Bounds.Width * window.DesktopScaling) <= area.Right &&
            window.Position.Y + Math.Ceiling(window.Bounds.Height * window.DesktopScaling) <= area.Bottom;
    }

    // Keep Avalonia's real HeadlessScreensStub and its screen-selection logic. Only its
    // cached monitor list changes; no production geometry helper or handler is mocked.
    private sealed class TestScreenLayout : IDisposable
    {
        private readonly IWindowImpl _window;
        private readonly object _implementation;
        private readonly Type _headlessScreenType;
        private readonly FieldInfo _allScreens;
        private readonly FieldInfo _screenCount;
        private readonly object? _previousScreens;
        private readonly object? _previousCount;
        private readonly FieldInfo _renderScaling;

        public TestScreenLayout(MainWindow window, params Screen[] screens)
        {
            _window = window.PlatformImpl!;
            _headlessScreenType = window.Screens.All[0].GetType();
            _implementation = typeof(Screens).GetField("_iScreenImpl", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(window.Screens)!;
            var screenBase = _implementation.GetType().BaseType!;
            _allScreens = screenBase.GetField("_allScreens", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _screenCount = screenBase.GetField("_screenCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _previousScreens = _allScreens.GetValue(_implementation);
            _previousCount = _screenCount.GetValue(_implementation);
            _renderScaling = _window.GetType().GetField("<RenderScaling>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ReplaceScreens(screens);
        }

        public void Change(params Screen[] screens)
        {
            ReplaceScreens(screens);
            (_implementation.GetType().GetProperty("Changed")!.GetValue(_implementation) as Action)?.Invoke();
        }

        private void ReplaceScreens(Screen[] screens)
        {
            var list = Array.CreateInstance(_allScreens.FieldType.GenericTypeArguments[0], screens.Length);
            for (var i = 0; i < screens.Length; i++)
            {
                var headless = (Screen)Activator.CreateInstance(_headlessScreenType,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [i + 1], null)!;
                typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(headless, screens[i].Bounds);
                typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(headless, screens[i].WorkingArea);
                typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(headless, screens[i].Scaling);
                typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(headless, screens[i].IsPrimary);
                list.SetValue(headless, i);
            }
            _allScreens.SetValue(_implementation, list);
            _screenCount.SetValue(_implementation, screens.Length);
        }

        public void SetDpi(double scaling)
        {
            _renderScaling.SetValue(_window, scaling);
            (_window.GetType().GetProperty("ScalingChanged")!.GetValue(_window) as Action<double>)?.Invoke(scaling);
        }

        public void Dispose()
        {
            _renderScaling.SetValue(_window, 1d);
            _allScreens.SetValue(_implementation, _previousScreens);
            _screenCount.SetValue(_implementation, _previousCount);
        }
    }
}
