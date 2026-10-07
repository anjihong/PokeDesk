using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace DeskPokemon;

public partial class MainWindow
{
    private ScaleTransform Zoom => (ScaleTransform)StageZoom.LayoutTransform!;
    private readonly HashSet<Key> _localKeys = new();

    private void OnGlobalInput() => Dispatcher.UIThread.Post(() =>
    {
        if (_closed || IsEvolutionTestMode) return;
        if (!_evolving) _animations["Bounce"].Play();
        AddExp();
    });

    private void OnInputStatusChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed) RefreshInputStatus();
    });

    private void RefreshInputStatus()
    {
        InputNotice.IsVisible = _hook != null && !_hook.IsActive;
        InputPermissionActions.IsVisible = OperatingSystem.IsMacOS() && _hook != null;
        InputAppIdentity.IsVisible = InputPermissionActions.IsVisible;
        InputAppPath.Text = InputApplicationPath(Environment.ProcessPath);
        InputStatusText.Text = _hook?.Status == InputHookStatus.PermissionRequired
            ? "현재 실행 중인 앱의 입력 모니터링 허용을 확인하지 못했습니다. 입력 내용은 저장하지 않습니다.\n" + _hook.StatusMessage
            : _hook?.StatusMessage;
    }

    private void OnRetryInput(object? sender, RoutedEventArgs e)
    {
        _hook?.Retry();
        RefreshInputStatus();
    }

    private void OnRequestInputPermission(object? sender, RoutedEventArgs e)
    {
        _hook?.RequestPermissionAndRetry();
        RefreshInputStatus();
    }

    private async void OnOpenInputSettings(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            if (await Launcher.LaunchUriAsync(new Uri(
                "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"))) return;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        if (!_closed) InputStatusText.Text = "시스템 설정 → 개인정보 보호 및 보안 → 입력 모니터링을 열어 주세요.";
    }

    // Show the exact running copy rather than another build with the same display name.
    internal static string InputApplicationPath(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return "실행 경로를 확인할 수 없습니다.";
        const string bundleExecutable = ".app/Contents/MacOS/";
        int bundle = executable.LastIndexOf(bundleExecutable, StringComparison.Ordinal);
        return bundle >= 0 ? executable[..(bundle + 4)] : executable;
    }

    private async void OnRetrySprite(object? sender, RoutedEventArgs e)
    {
        if (_closed || _evolving) return;
        await LoadPokemonAsync(_settings.SelectedDex, _settings.SelectedShiny);
        if (_closed) return;
        if (CheckedGen() is { } gen)
        {
            var tab = GenTabs.Children.OfType<Avalonia.Controls.RadioButton>().First(t => (int)t.Tag! == gen);
            OnGenChecked(tab, e);
        }
        if (_eggState is EggState.Waiting or EggState.Ready) await LoadEggAssetsAsync();
        if (_crackFrames == null) await LoadCrackAssetsAsync();
    }

    // Without OS permission, inputs inside our own window still work, without double-counting a live hook.
    private void OnLocalPointer(object? sender, PointerPressedEventArgs e)
    {
        if (_hook?.IsActive != true && _startServices) OnGlobalInput();
    }

    private void OnLocalKey(object? sender, KeyEventArgs e)
    {
        if (_hook?.IsActive != true && _startServices && _localKeys.Add(e.Key)) OnGlobalInput();
    }

    private void OnLocalKeyUp(object? sender, KeyEventArgs e) => _localKeys.Remove(e.Key);

    private void ShowSpriteError(string message)
    {
        if (_closed) return;
        SpriteStatus.Text = message;
        SpriteStatus.IsVisible = true;
    }

    private static void DisposeFrames(Dictionary<string, SpriteFrame>? frames)
    {
        if (frames == null) return;
        foreach (var frame in frames.Values) frame.Bitmap.Dispose();
    }

    internal void DiscardSaveOnClose() => _discardSave = true;
}
