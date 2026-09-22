using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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
        settings.For(4).Exp = 5;
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

    private static void Capture(Window window, string name, int scale)
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
    }
}
