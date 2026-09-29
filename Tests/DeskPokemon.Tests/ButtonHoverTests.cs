using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData("button")]
    [InlineData("menu")]
    [InlineData("generation")]
    [InlineData("pokemon")]
    [InlineData("starter")]
    [InlineData("checkbox")]
    public async Task RaisedArtworkKeepsTheOriginalBottomEdgeClickableAndKeyboardFocusVisible(string kind)
    {
        var main = new MainWindow(Settings.NewPreview(4), false);
        var starter = new StarterWindow([1, 4, 7], false);
        Button button = kind switch
        {
            "menu" => new ToggleButton { Theme = (ControlTheme)main.Resources["MenuTab"]! },
            "generation" => new RadioButton { Theme = (ControlTheme)main.Resources["GenTab"]! },
            "pokemon" => new RadioButton { Theme = (ControlTheme)main.Resources["IconButton"]! },
            "starter" => new Button { Theme = (ControlTheme)starter.Resources["Choice"]! },
            "checkbox" => new CheckBox(),
            _ => new Button(),
        };
        var label = new TextBlock { Text = "버튼" };
        button.Content = label;
        var window = new Window { Width = 280, Height = 120, Content = new StackPanel
        {
            Margin = new Thickness(12), Children = { button },
        }};
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var visual = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Lift");
            var focus = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Focus");
            var plate = button.GetVisualDescendants().OfType<PixelSurface>().First();
            var asset = plate.Asset;
            var originalBounds = button.Bounds;
            var labelOrigin = label.TranslatePoint(default, window)!.Value;
            // This strip is inside the fixed hit box but below the raised artwork.
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height - .25), window)!.Value;
            window.MouseMove(point);
            await WaitForLift(-2);
            Assert.True(button.IsPointerOver);
            Assert.Equal(originalBounds, button.Bounds);
            Assert.Equal(asset, plate.Asset);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(focus.BorderBrush).Color.A);
            if (kind == "checkbox") Assert.Equal(labelOrigin, label.TranslatePoint(default, window)!.Value);

            window.MouseDown(point, MouseButton.Left);
            await WaitForLift(0);
            window.MouseUp(point, MouseButton.Left);
            await WaitForLift(-2);
            Assert.Equal(1, clicks);
            Assert.True(button.IsPointerOver);
            Assert.Equal(originalBounds, button.Bounds);

            window.MouseMove(new Point(275, 115));
            await WaitForLift(0);
            window.FocusManager!.ClearFocus();
            button.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            Assert.NotEqual(0, Assert.IsAssignableFrom<ISolidColorBrush>(focus.BorderBrush).Color.A);

            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver); // A separate focus adorner must not intercept the pointer.
            await WaitForLift(-2);
            button.IsEnabled = false;
            await WaitForLift(0);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, clicks);

            async Task WaitForLift(double target)
            {
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(3))
                {
                    Dispatcher.UIThread.RunJobs();
                    if (Math.Abs((visual.RenderTransform?.Value.M32 ?? 0) - target) < .001) return;
                    await Task.Delay(16);
                }
                Assert.Equal(target, visual.RenderTransform?.Value.M32 ?? 0, 3);
            }
        }
        finally { window.Close(); main.Close(); starter.Close(); }
    }
}
