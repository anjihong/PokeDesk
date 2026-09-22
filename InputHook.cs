using DeskPokemon.Platform;

namespace DeskPokemon;

public enum InputHookStatus
{
    Starting,
    Active,
    PermissionRequired,
    Unavailable,
    Disposed
}

/// <summary>
/// Reports only keyboard/button press counts, never typed text. Native callbacks run on a
/// worker thread; UI subscribers must dispatch onto their UI thread.
/// </summary>
public sealed class InputHook : IDisposable
{
    private readonly NativeInputMonitor _monitor;

    public event Action? Triggered;
    public event Action? StatusChanged;

    public InputHookStatus Status => _monitor.Status;
    public string StatusMessage => _monitor.StatusMessage;
    public bool IsActive => Status == InputHookStatus.Active;
    public bool CanRequestPermission => OperatingSystem.IsMacOS() &&
        Status is InputHookStatus.PermissionRequired or InputHookStatus.Unavailable;

    public InputHook()
    {
        _monitor = OperatingSystem.IsWindows() ? new WindowsInputMonitor() :
            OperatingSystem.IsMacOS() ? new MacInputMonitor() : new UnsupportedInputMonitor();
        _monitor.Triggered += OnTriggered;
        _monitor.StatusChanged += OnStatusChanged;
        _monitor.Start();
    }

    /// <summary>Recheck permissions/reinstall unavailable hooks, without displaying a permission prompt.</summary>
    public void Retry() => _monitor.Retry();

    /// <summary>Call from an explicit user action. macOS may show its Input Monitoring permission prompt.</summary>
    public void RequestPermissionAndRetry() => _monitor.RequestPermissionAndRetry();

    private void OnTriggered() => Triggered?.Invoke();
    private void OnStatusChanged() => StatusChanged?.Invoke();

    public void Dispose()
    {
        _monitor.Triggered -= OnTriggered;
        _monitor.StatusChanged -= OnStatusChanged;
        _monitor.Dispose();
    }
}
