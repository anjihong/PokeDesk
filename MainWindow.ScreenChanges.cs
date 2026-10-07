using Avalonia;
using Avalonia.Threading;

namespace DeskPokemon;

public partial class MainWindow
{
    private readonly record struct PresentationScreen(PixelRect Bounds, PixelRect WorkingArea,
        double DesktopScaling, double RenderScaling);

    private PresentationScreen _presentationScreen;
    private bool _screenRefitPending;

    private PresentationScreen CurrentPresentationScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        return new(screen?.Bounds ?? WorkingArea, screen?.WorkingArea ?? WorkingArea,
            DesktopScaling, RenderScaling);
    }

    private void InitializeScreenTracking()
    {
        _presentationScreen = CurrentPresentationScreen();
        PositionChanged += OnPresentationScreenChanged;
        ScalingChanged += OnPresentationScreenChanged;
        Screens.Changed += OnPresentationScreenChanged;
    }

    private void StopScreenTracking()
    {
        PositionChanged -= OnPresentationScreenChanged;
        ScalingChanged -= OnPresentationScreenChanged;
        Screens.Changed -= OnPresentationScreenChanged;
    }

    private void OnPresentationScreenChanged(object? sender, EventArgs e)
    {
        if (_closed) return;
        if (!_placed)
        {
            _presentationScreen = CurrentPresentationScreen();
            return;
        }
        if (_screenRefitPending) return;
        _screenRefitPending = true;
        // A monitor move can report position, DPI and work area separately. Read their
        // settled values after layout, and do not refit for our own positioning events.
        Dispatcher.UIThread.Post(() =>
        {
            _screenRefitPending = false;
            if (_closed || !_placed) return;
            var screen = CurrentPresentationScreen();
            if (screen == _presentationScreen) return;
            var previous = _presentationScreen;
            _presentationScreen = screen; // Publish before moving the window to prevent feedback loops.
            if (Position != _lastDrawerPosition || previous.DesktopScaling != screen.DesktopScaling)
                _positionBeforeDrawer = null;
            ApplyPresentation();
            // A work-area change can alter the viewport without changing UI scale or
            // the panels' natural size, so explicitly retarget an open/closing drawer.
            if (IsDrawerOpen) AnimateDrawer(open: true);
            else if (Drawer.Height > 0) AnimateDrawer(open: false);
            ClampToScreen();
        }, DispatcherPriority.Loaded);
    }
}
