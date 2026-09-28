using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task PanelCaptureWaitsPastAConstrainedPartialAnimationFrame()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            var opening = OpenPanelAsync(window, "settings");
            SeekDrawerFrameWithoutClock(window, .07);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var drawer = window.FindControl<Border>("Drawer")!;
            var target = Field<double>(window, "_drawerTargetHeight");
            Assert.InRange(drawer.Height, 1, target - 1);

            // Stop the real clock at a partial frame: the capture helper must remain
            // pending even after multiple layout/poll cycles report that clipped size.
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(opening.IsCompleted);

            SeekDrawerFrameWithoutClock(window, .25);
            await opening;
            Assert.Equal(Field<double>(window, "_drawerTargetHeight"), drawer.Bounds.Height, 3);
            Assert.Equal(window.FindControl<LayoutTransformControl>("UiZoom")!.Bounds.Size, window.Bounds.Size);
        }
        finally { window.Close(); }
    }
}
