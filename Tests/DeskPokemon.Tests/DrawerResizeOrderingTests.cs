using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ClosingDrawerKeepsOriginUntilTheFinalResizeEventIsHandled(int topOffset)
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        var positionHandler = typeof(MainWindow).GetMethod("OnSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .CreateDelegate<EventHandler<SizeChangedEventArgs>>(window);
        var delayed = new List<SizeChangedEventArgs>();
        void CaptureResize(object? sender, SizeChangedEventArgs args) => delayed.Add(args);
        try
        {
            var (tab, drawer, area, _) = PreparePositionedDrawer(window);
            var baseline = window.Bounds.Size;
            var origin = new PixelPoint(area.X + 127, area.Y + topOffset);
            window.Position = origin;
            await OpenPanelAsync(window, "dex");
            Assert.Equal(area.Y, window.Position.Y);
            var before = $"size={window.Bounds.Size}, last={Field<Size>(window, "_lastPositionedSize")}, origin={Field<PixelPoint?>(window, "_positionBeforeDrawer")}";

            // Model a native SizeToContent resize whose positioning notification is
            // delivered after the animation completion callback has already run.
            typeof(MainWindow).GetField("_placed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            window.SizeChanged += CaptureResize;
            tab.IsChecked = false;
            SeekDrawerFrameWithoutClock(window, .25);
            var timeline = Field<Timeline>(window, "_drawerAnimation");
            ((Action)typeof(Timeline).GetField("_completed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(timeline)!)();
            await EventuallyAsync(window, () => window.Bounds.Size == baseline && delayed.Count > 0);
            Assert.Equal(0, drawer.Height);
            Assert.True(origin == Field<PixelPoint?>(window, "_positionBeforeDrawer"), $"before {before}; after size={window.Bounds.Size}, last={Field<Size>(window, "_lastPositionedSize")}, origin={Field<PixelPoint?>(window, "_positionBeforeDrawer")}, delayed={delayed.Count}");

            window.SizeChanged -= CaptureResize;
            typeof(MainWindow).GetField("_placed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            foreach (var notification in delayed) positionHandler(window, notification);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(origin, window.Position);
            Assert.Null(Field<PixelPoint?>(window, "_positionBeforeDrawer"));
        }
        finally
        {
            window.SizeChanged -= CaptureResize;
            window.Close();
        }
    }

    private static Task WaitForDrawerCloseAsync(MainWindow window) => EventuallyAsync(window, () =>
        window.FindControl<Border>("Drawer")!.Height == 0 &&
        window.FindControl<Border>("Drawer")!.Bounds.Height == 0 &&
        window.Bounds.Size == window.FindControl<LayoutTransformControl>("UiZoom")!.Bounds.Size &&
        Field<PixelPoint?>(window, "_positionBeforeDrawer") == null);
}
