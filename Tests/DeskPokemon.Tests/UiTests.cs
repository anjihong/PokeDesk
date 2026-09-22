using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DeskPokemon.Tests;

public class UiTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void StarterUsesBundledKoreanFontAndDeliversSelectedDex(int scale)
    {
        var window = new StarterWindow([1, 4, 7], false);
        using var icon = TestSprite();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
            Assert.Equal(TextRenderingMode.Antialias, RenderOptions.GetTextRenderingMode(window));
            Assert.Contains("NanumGothic", window.FontFamily.Name);
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
            var choices = window.FindControl<StackPanel>("Choices")!;
            foreach (var image in choices.GetVisualDescendants().OfType<Image>()) image.Source = icon;
            Assert.Equal(3, choices.Children.Count);
            Assert.InRange(window.Bounds.Width, 330, 410);
            Assert.InRange(window.Bounds.Height, 110, 180);
            Capture(window, "starter", scale);

            int? selected = null;
            window.Selected += dex => selected = dex;
            ((Button)choices.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, selected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharedMainViewOpensDexAndRestoresCollapsedHeight(int scale)
    {
        var settings = Settings.New(4);
        settings.For(4).Exp = 15;
        var window = new MainWindow(settings, false);
        using var icon = TestSprite();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Invoke(window, "BuildGenTabs");
            PopulatePet(window, icon);
            var transform = window.FindControl<Image>("Sprite")!.TransformToVisual(window)!.Value;
            Assert.Equal(3.5, transform.M11, 6);
            Assert.Equal(3.5, transform.M22, 6);
            Assert.Equal(2, window.FindControl<Canvas>("EggStage")!.TransformToVisual(window)!.Value.M11, 6);
            var icons = Enumerable.Range(1, 30).ToDictionary(dex => dex, _ => icon);
            Invoke(window, "RebuildIconGrid", 1, icons);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(30, grid.Children.Count);
            var unowned = (RadioButton)grid.Children[0];
            Assert.False(unowned.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(unowned));
            Assert.True(((RadioButton)grid.Children[3]).IsChecked);
            Assert.Equal("Lv. 1", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal(50, window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.Width);
            Assert.True(window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.IsVisible);
            Assert.Equal(9, window.FindControl<StackPanel>("GenTabs")!.Children.Count);
            Assert.Equal(300, window.Bounds.Width);
            Assert.True(window.Topmost);
            Assert.Equal(TextRenderingMode.Antialias, RenderOptions.GetTextRenderingMode(window));
            Assert.False(window.ShowInTaskbar);
            Assert.False(window.FindControl<Border>("InputNotice")!.IsVisible);
            var closedHeight = window.Bounds.Height;
            Capture(window, "main-collapsed", scale);

            var menu = window.FindControl<StackPanel>("MenuTabs")!;
            var dex = (ToggleButton)menu.Children[0];
            dex.IsChecked = true;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(window.FindControl<Border>("Drawer")!.Height >= 180);
            Assert.True(window.Bounds.Height > closedHeight + 150);
            Capture(window, "main-dex", scale);

            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            Invoke(window, "RebuildIconGrid", 1, icons);
            Assert.Single(grid.Children);
            Assert.Equal(4, ((RadioButton)grid.Children[0]).Tag);
            Capture(window, "main-owned", scale);

            dex.IsChecked = false;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, window.FindControl<Border>("Drawer")!.Height);
            Assert.Equal(closedHeight, window.Bounds.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EggResultRemovesEggLayoutAndShowsTheSameResultView()
    {
        var window = new MainWindow(Settings.New(4), false);
        using var icon = TestSprite();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var stateType = typeof(MainWindow).GetNestedType("EggState", BindingFlags.NonPublic)!;
            Assert.False(window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.IsVisible);
            PopulatePet(window, icon);
            Invoke(window, "SetEggState", Enum.Parse(stateType, "Result"));
            window.FindControl<TextBlock>("BubbleText")!.Text = "파이리";
            window.FindControl<Image>("ResultImage")!.Source = icon;
            window.UpdateLayout();
            Assert.False(window.FindControl<Canvas>("EggStage")!.IsVisible);
            Assert.False(window.FindControl<LayoutTransformControl>("EggZoom")!.IsVisible);
            Assert.True(window.FindControl<Image>("ResultImage")!.IsVisible);
            Capture(window, "egg-result", 2);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExperienceTooltipOpensOnHoverAndUpdatesInPlaceWhenLevelChanges()
    {
        var settings = Settings.New(4);
        settings.For(4).Exp = 29;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            var track = window.FindControl<Border>("ExpTrack")!;
            var tooltip = Assert.IsType<ToolTip>(ToolTip.GetTip(track));
            Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(track));
            Assert.Equal(300, ToolTip.GetShowDelay(track));
            Assert.Equal("현재 경험치: 29\n필요 경험치: 30", tooltip.Content);
            Assert.False(ToolTip.GetIsOpen(track));

            Hover(window, track);
            Assert.True(track.IsPointerOver);
            await EventuallyAsync(window, () => ToolTip.GetIsOpen(track));
            Assert.NotNull(tooltip.GetVisualRoot());
            await EventuallyAsync(window, () => tooltip.Bounds.Width > 0 && tooltip.Bounds.Height > 0 && tooltip.Opacity == 1);
            Capture(tooltip, "exp-tooltip", 1);
            Capture(tooltip, "exp-tooltip", 2);

            Invoke(window, "AddExp");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(2, settings.For(4).Level);
            Assert.Equal(0, settings.For(4).Exp);
            Assert.Equal("Lv. 2", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Same(tooltip, ToolTip.GetTip(track));
            Assert.True(ToolTip.GetIsOpen(track));
            Assert.Equal("현재 경험치: 0\n필요 경험치: 60", tooltip.Content);
            Assert.False(window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.IsVisible);

            // The same already-visible tooltip must continue updating, without a second hover.
            Invoke(window, "AddExp");
            Assert.Same(tooltip, ToolTip.GetTip(track));
            Assert.True(ToolTip.GetIsOpen(track));
            Assert.Equal("현재 경험치: 1\n필요 경험치: 60", tooltip.Content);

            window.MouseMove(new Point(-1, -1), RawInputModifiers.None);
            await EventuallyAsync(window, () => !ToolTip.GetIsOpen(track));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EmptyExperienceBarStillHasAFullWidthHoverTarget()
    {
        var window = new MainWindow(Settings.New(4), false);
        try
        {
            ShowAndLayout(window);
            var track = window.FindControl<Border>("ExpTrack")!;
            var bar = window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!;
            Assert.Equal(0, bar.Width);
            Assert.False(bar.IsVisible);
            Assert.Equal(100, track.Bounds.Width);

            // Hover near the right edge of the background track, where no fill exists.
            Hover(window, track, new Point(track.Bounds.Width - 2, track.Bounds.Height / 2));
            Assert.True(track.IsPointerOver);
            await EventuallyAsync(window, () => ToolTip.GetIsOpen(track));
            var tooltip = Assert.IsType<ToolTip>(ToolTip.GetTip(track));
            Assert.Equal("현재 경험치: 0\n필요 경험치: 30", tooltip.Content);
            Assert.NotNull(tooltip.GetVisualRoot());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DrawerMovesUpByItsExpansionAndReturnsToEachChosenPositionWithoutDrift()
    {
        var window = new MainWindow(Settings.New(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var origin = new PixelPoint(area.X + 137 + cycle * 17,
                    area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 73 + cycle * 11);
                window.Position = origin;
                Assert.Equal(origin, window.Position);

                tab.IsChecked = true;
                await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
                Assert.True(window.Bounds.Height > baselineHeight + 150);
                Assert.Equal(origin.X, window.Position.X);
                Assert.Equal(origin.Y - (int)Math.Round((window.Bounds.Height - baselineHeight) * window.DesktopScaling),
                    window.Position.Y);

                tab.IsChecked = false;
                await EventuallyAsync(window, () => drawer.Height == 0);
                Assert.Equal(baselineHeight, window.Bounds.Height);
                Assert.Equal(origin, window.Position);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MovingAnOpenDrawerThenResizingThePetKeepsTheNewBottomAnchor()
    {
        var window = new MainWindow(Settings.New(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var origin = new PixelPoint(area.X + 123,
                area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 160);
            window.Position = origin;
            tab.IsChecked = true;
            await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);

            var draggedPosition = new PixelPoint(origin.X + 61, window.Position.Y + 97);
            window.Position = draggedPosition;
            Assert.Equal(draggedPosition, window.Position);
            var heightBeforePetResize = window.Bounds.Height;
            var widthBeforePetResize = window.Bounds.Width;

            // A sprite change can resize the pet while its drawer remains open.
            // Change the real measured content, not the positioning callback or anchor fields.
            window.FindControl<Canvas>("Stage")!.Height += 18.25;
            await EventuallyAsync(window, () => window.Bounds.Height > heightBeforePetResize);
            var resizedOpenHeight = window.Bounds.Height;
            var resizedOpenPosition = window.Position;
            Assert.Equal(widthBeforePetResize, window.Bounds.Width);
            Assert.Equal(draggedPosition.X, resizedOpenPosition.X);
            Assert.Equal(draggedPosition.Y - (int)Math.Round(
                (resizedOpenHeight - heightBeforePetResize) * window.DesktopScaling), resizedOpenPosition.Y);

            tab.IsChecked = false;
            await EventuallyAsync(window, () => drawer.Height == 0);
            Assert.True(resizedOpenHeight > window.Bounds.Height);
            Assert.Equal(draggedPosition.X, window.Position.X);
            Assert.Equal(resizedOpenPosition.Y + (int)Math.Round(
                (resizedOpenHeight - window.Bounds.Height) * window.DesktopScaling), window.Position.Y);
            Assert.NotEqual(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReversingTheDrawerMidAnimationKeepsTheOriginalPositionAnchor()
    {
        var window = new MainWindow(Settings.New(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            var origin = new PixelPoint(area.X + 151,
                area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 81);
            window.Position = origin;
            tab.IsChecked = true;

            // Freeze the real timeline at an intermediate frame, then let headless
            // resize events settle without racing its wall-clock completion.
            SeekDrawerFrameWithoutClock(window, .07);
            await EventuallyAsync(window, () => window.Bounds.Height > baselineHeight);
            Assert.True(drawer.Height < expansion);
            Assert.InRange(window.Position.Y, area.Y + 1, origin.Y - 1);
            var partialHeight = window.Bounds.Height;
            tab.IsChecked = false;

            SeekDrawerFrameWithoutClock(window, .10);
            await EventuallyAsync(window, () => window.Bounds.Height < partialHeight);
            Assert.True(window.Bounds.Height > baselineHeight);
            tab.IsChecked = true;
            await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
            Assert.Equal(origin.X, window.Position.X);
            Assert.Equal(origin.Y - (int)Math.Round((window.Bounds.Height - baselineHeight) * window.DesktopScaling),
                window.Position.Y);

            tab.IsChecked = false;
            await EventuallyAsync(window, () => drawer.Height == 0);
            Assert.Equal(baselineHeight, window.Bounds.Height);
            Assert.Equal(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DrawerClampsAtTheScreenTopAndStillReturnsToItsOriginalPosition()
    {
        var window = new MainWindow(Settings.New(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            var origin = new PixelPoint(area.X + 127, area.Y + 5);
            window.Position = origin;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                tab.IsChecked = true;
                await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
                Assert.Equal(origin.X, window.Position.X);
                Assert.Equal(area.Y, window.Position.Y);

                tab.IsChecked = false;
                await EventuallyAsync(window, () => drawer.Height == 0);
                Assert.Equal(baselineHeight, window.Bounds.Height);
                Assert.Equal(origin, window.Position);
            }
        }
        finally { window.Close(); }
    }

    private static void ShowAndLayout(MainWindow window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Hover(MainWindow window, Control control, Point? point = null)
    {
        var local = point ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var position = control.TranslatePoint(local, window);
        Assert.NotNull(position);
        window.MouseMove(position.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(MainWindow window, Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (condition()) return;
            await Task.Delay(20);
        } while (timeout.Elapsed < TimeSpan.FromSeconds(3));
        Assert.True(condition(), "The expected UI state was not reached within three seconds.");
    }

    private static void SeekDrawerFrameWithoutClock(MainWindow window, double seconds)
    {
        var animation = (Timeline)typeof(MainWindow).GetField("_drawerAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;
        animation.Dispose(); // Stop automatic ticks; the next toggle replaces this timeline normally.
        typeof(Timeline).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(animation, [seconds]);
    }

    private static (ToggleButton Tab, Border Drawer, PixelRect Area, double Expansion) PreparePositionedDrawer(MainWindow window)
    {
        ShowAndLayout(window);
        var drawer = window.FindControl<Border>("Drawer")!;
        var content = window.FindControl<Border>("DrawerContent")!;
        content.Measure(new Size(300, double.PositiveInfinity));
        var expansion = content.DesiredSize.Height;
        var area = (PixelRect)typeof(MainWindow).GetProperty("WorkingArea", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;
        // startServices=false deliberately skips native startup; enable its positioning
        // path after initial layout. This tests the headless backend's real DesktopScaling,
        // not the 2x bitmap export used by the screenshot tests above.
        typeof(MainWindow).GetField("_placed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        var tab = (ToggleButton)window.FindControl<StackPanel>("MenuTabs")!.Children[0];
        return (tab, drawer, area, expansion);
    }

    private static void Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

    // Original, deterministic test pixels: no network, cache, random choice, or external artwork.
    private static Bitmap TestSprite()
    {
        var pixels = new SpritePixels(24, 30);
        for (var y = 2; y < 29; y++)
        for (var x = 2; x < 22; x++)
        {
            if (y < 8 && (x < 5 || x > 18)) continue;
            var offset = y * pixels.Stride + x * 4;
            var eye = y is 10 or 11 && x is 7 or 16;
            pixels.Pixels[offset] = eye ? (byte)30 : (byte)60;
            pixels.Pixels[offset + 1] = eye ? (byte)30 : (byte)(y > 18 ? 210 : 135);
            pixels.Pixels[offset + 2] = eye ? (byte)30 : (byte)240;
            pixels.Pixels[offset + 3] = 255;
        }
        return pixels.ToBitmap();
    }

    private static void PopulatePet(MainWindow window, Bitmap icon)
    {
        var sprite = window.FindControl<Image>("Sprite")!;
        sprite.Source = icon;
        sprite.Width = 24;
        sprite.Height = 30;
        var stage = window.FindControl<Canvas>("Stage")!;
        stage.Width = 24;
        stage.Height = 30;
        var zoom = (ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!;
        zoom.ScaleX = zoom.ScaleY = 3.5;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Control window, string name, int scale)
    {
        window.UpdateLayout();
        var size = window.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DeskPokemon.csproj"))) root = root.Parent;
        var output = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "artifacts", "test-results", "screenshots");
        Directory.CreateDirectory(output);
        bitmap.Save(Path.Combine(output, $"{name}-{scale}x.png"));
        var layout = new List<object>();
        CaptureLayout(window, "root", layout);
        File.WriteAllText(Path.Combine(output, $"{name}-{scale}x.layout.json"),
            JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CaptureLayout(Visual visual, string path, List<object> layout)
    {
        if (!visual.IsVisible) return;
        var bounds = visual.Bounds;
        var matrix = visual.RenderTransform?.Value ?? Matrix.Identity;
        layout.Add(new
        {
            path,
            type = visual.GetType().Name,
            name = (visual as Control)?.Name,
            bounds = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }.Select(v => Math.Round(v, 3)).ToArray(),
            text = (visual as TextBlock)?.Text,
            font = (visual as TextBlock)?.FontFamily.Name,
            fontSize = (visual as TextBlock)?.FontSize,
            foreground = (visual as TextBlock)?.Foreground?.ToString(),
            background = (visual as Border)?.Background?.ToString(),
            opacity = Math.Round(visual.Opacity, 3),
            clip = visual.ClipToBounds,
            transform = new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32 }
                .Select(v => Math.Round(v, 3)).ToArray(),
        });
        var children = visual.GetVisualChildren().ToArray();
        for (var i = 0; i < children.Length; i++) CaptureLayout(children[i], $"{path}/{i}", layout);
    }
}
