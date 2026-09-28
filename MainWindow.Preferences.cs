using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DeskPokemon;

public partial class MainWindow
{
    private IStartupRegistration? _startupRegistration;
    private bool _updatingPreferences = true;
    private bool _updatingPresentation;
    private double UiScaleFactor => ((ScaleTransform)UiZoom.LayoutTransform!).ScaleX;

    private void InitializePreferences()
    {
        DexPanel.SizeChanged += OnDrawerContentSizeChanged;
        SettingsPanel.SizeChanged += OnDrawerContentSizeChanged;
        FlipCheckBox.IsChecked = _settings.FlipHorizontal;
        RefreshStartupStatus();
        ApplyPresentation();
        _updatingPreferences = false;
    }

    private void OnFlipChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingPreferences) return;
        var previous = _settings.FlipHorizontal;
        _settings.FlipHorizontal = FlipCheckBox.IsChecked == true;
        if (!TrySaveSettings())
        {
            _settings.FlipHorizontal = previous;
            _updatingPreferences = true;
            FlipCheckBox.IsChecked = previous;
            _updatingPreferences = false;
        }
        ApplyPresentation();
    }

    private void OnStartupChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingPreferences || _startupRegistration == null) return;
        var result = _startupRegistration.SetEnabled(StartupCheckBox.IsChecked == true);
        ShowStartupState(result);
    }

    private void RefreshStartupStatus()
    {
        if (_startupRegistration == null)
        {
            StartupCheckBox.IsEnabled = false;
            StartupStatus.Text = "미리보기에서는 자동 시작 설정을 변경하지 않습니다.";
            return;
        }
        ShowStartupState(_startupRegistration.Query());
    }

    private void ShowStartupState(StartupRegistrationResult result)
    {
        var previous = _updatingPreferences;
        _updatingPreferences = true;
        StartupCheckBox.IsEnabled = result.IsSupported;
        StartupCheckBox.IsChecked = result.IsEnabled;
        StartupStatus.Text = result.Error ?? (result.IsEnabled
            ? "다음 로그인부터 이 앱이 자동으로 시작됩니다."
            : "로그인할 때 자동으로 실행할 수 있습니다.");
        _updatingPreferences = previous;
    }

    private void OnScaleDown(object? sender, RoutedEventArgs e) => ChangeUiScale(-2);
    private void OnScaleUp(object? sender, RoutedEventArgs e) => ChangeUiScale(2);

    private void ChangeUiScale(int delta)
    {
        var previous = _settings.UiScale;
        _settings.UiScale = Math.Clamp(previous + delta, 2, 8);
        if (_settings.UiScale == previous) return;
        if (!TrySaveSettings()) _settings.UiScale = previous;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (_updatingPresentation) return;
        _updatingPresentation = true;
        try
        {
            ((ScaleTransform)PetFacing.RenderTransform!).ScaleX = _settings.FlipHorizontal && !SpriteMissing.IsVisible ? -1 : 1;
            var requested = _settings.UiScale / 2.0;
            var area = WorkingArea;
            var availableWidth = Math.Max(1, area.Width / DesktopScaling - 20);
            var availableHeight = Math.Max(1, area.Height / DesktopScaling - 20);
            var collapsed = Root.Bounds.Height > 0 ? Root.Bounds.Height - Drawer.Height : 210;
            // Reserve a usable drawer viewport even at 8x on a small monitor.
            var limit = Math.Min(availableWidth / Root.Width, availableHeight / Math.Max(1, collapsed + 100));
            var scale = Math.Max(.25, Math.Min(requested, Math.Floor(limit * 4) / 4));
            var zoom = (ScaleTransform)UiZoom.LayoutTransform!;
            var changed = Math.Abs(zoom.ScaleX - scale) > .001;
            var open = MenuTabs.Children.OfType<ToggleButton>().Any(tab => tab.IsChecked == true);
            if (changed)
            {
                _drawerAnimation?.Dispose();
                _positionBeforeDrawer = null;
                zoom.ScaleX = zoom.ScaleY = scale;
            }
            ConfigureDrawerViewport();
            ScaleValue.Text = $"{_settings.UiScale}배" + (scale < requested ? " · 화면 맞춤" : "");
            ScaleDownButton.IsEnabled = _settings.UiScale > 2;
            ScaleUpButton.IsEnabled = _settings.UiScale < 8;
            if (open && changed)
            {
                DrawerContent.Measure(new Size(Root.Width, double.PositiveInfinity));
                Drawer.Height = _drawerTargetHeight = DrawerContent.DesiredSize.Height;
                UpdateLayout();
                _positionBeforeDrawer = new PixelPoint(Position.X,
                    Position.Y + (int)Math.Round(Drawer.Height * scale * DesktopScaling));
                _heightBeforeDrawer = Bounds.Height - Drawer.Height * scale;
                _lastDrawerPosition = Position;
            }
            if (_placed) ClampToScreen();
        }
        finally { _updatingPresentation = false; }
    }

    private void ConfigureDrawerViewport()
    {
        var scale = Math.Max(.25, UiScaleFactor);
        var collapsed = Root.Bounds.Height > 0 ? Root.Bounds.Height - Drawer.Height : 210;
        var available = WorkingArea.Height / (DesktopScaling * scale) - collapsed - 20;
        DrawerScroll.MaxHeight = Math.Clamp(available, 80, 480);
    }
}
