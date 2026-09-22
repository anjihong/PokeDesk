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
        if (_closed) return;
        _animations["Bounce"].Play();
        AddExp();
    });

    private void OnInputStatusChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed) RefreshInputStatus();
    });

    private void RefreshInputStatus()
    {
        InputNotice.IsVisible = _hook != null && !_hook.IsActive;
        InputStatusText.Text = _hook?.Status == InputHookStatus.PermissionRequired
            ? "다른 앱에서 입력해도 경험치가 오르려면 입력 모니터링 권한이 필요합니다. 입력 내용은 저장하지 않습니다.\n" + _hook.StatusMessage
            : _hook?.StatusMessage;
    }

    private void OnRetryInput(object? sender, RoutedEventArgs e)
    {
        _hook?.RequestPermissionAndRetry();
        RefreshInputStatus();
    }

    private async void OnRetrySprite(object? sender, RoutedEventArgs e)
    {
        await LoadPokemonAsync(_settings.SelectedDex);
        if (_closed) return;
        if (CheckedGen() is { } gen)
        {
            var tab = GenTabs.Children.OfType<Avalonia.Controls.RadioButton>().First(t => (int)t.Tag! == gen);
            OnGenChecked(tab, e);
        }
        if (_eggFrames == null) await LoadEggAssetsAsync();
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
