using System.Diagnostics;

namespace DeskPokemon.Platform;

/// <summary>Owns a dedicated native message loop, so hooks do not depend on a particular UI framework.</summary>
internal abstract class NativeInputMonitor : IDisposable
{
    private sealed record State(InputHookStatus Status, string Message);

    private readonly AutoResetEvent _retry = new(true);
    private readonly object _lifecycle = new();
    private volatile State _state = new(InputHookStatus.Starting, "입력 감지를 시작하는 중입니다.");
    private Thread? _thread;
    private volatile bool _stopping;

    public event Action? Triggered;
    public event Action? StatusChanged;
    public InputHookStatus Status => _state.Status;
    public string StatusMessage => _state.Message;
    protected bool IsStopping => _stopping;

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_stopping || _thread is not null) return;
            _thread = new Thread(WorkerMain) { IsBackground = true, Name = "PokeDesk input monitor" };
            _thread.Start();
        }
    }

    public void Retry()
    {
        lock (_lifecycle)
        {
            if (!_stopping && Status != InputHookStatus.Active) _retry.Set();
        }
    }

    public virtual void RequestPermissionAndRetry() => Retry();
    protected abstract void RunMonitor();
    protected virtual void WakeNativeLoop() { }

    private void WorkerMain()
    {
        try
        {
            while (!_stopping)
            {
                _retry.WaitOne();
                if (_stopping) break;
                SetStatus(InputHookStatus.Starting, "입력 감지를 시작하는 중입니다.");
                if (_stopping) break; // A status subscriber may dispose this monitor.
                try
                {
                    RunMonitor();
                    if (Status == InputHookStatus.Active)
                        SetStatus(InputHookStatus.Unavailable, "입력 감지가 중지되었습니다. 다시 시도해 주세요.");
                }
                catch (Exception ex)
                {
                    // Native setup failures must not prevent the pet, collection, or settings from opening.
                    Debug.WriteLine($"Global input monitor failed: {ex}");
                    SetStatus(InputHookStatus.Unavailable, "전역 입력 감지를 시작하지 못했습니다. 다시 시도해 주세요.");
                }
            }
        }
        finally
        {
            // The worker owns this handle even when Dispose runs inside a native callback
            // or returns before a slow native callback has finished.
            _retry.Dispose();
        }
    }

    protected void SetStatus(InputHookStatus status, string message)
    {
        lock (_lifecycle)
        {
            if (_stopping) return;
            var next = new State(status, message);
            if (_state == next) return;
            _state = next;
        }
        InvokeSafely(StatusChanged);
    }

    protected void PublishTrigger()
    {
        if (!_stopping) InvokeSafely(Triggered);
    }

    // Exceptions must never cross an unmanaged callback boundary or break the native input chain.
    private static void InvokeSafely(Action? handler)
    {
        try { handler?.Invoke(); }
        catch (Exception ex) { Debug.WriteLine($"Input monitor subscriber failed: {ex}"); }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_stopping) return;
            _stopping = true;
            _state = new(InputHookStatus.Disposed, "입력 감지가 종료되었습니다.");
            _retry.Set();
            WakeNativeLoop();
            thread = _thread;
        }
        // Native loops release hooks/CF objects on their own thread. Do not join ourselves
        // if a non-UI subscriber disposes from a callback.
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(2));
        if (thread is null) _retry.Dispose();
    }
}

internal sealed class UnsupportedInputMonitor : NativeInputMonitor
{
    protected override void RunMonitor() => SetStatus(InputHookStatus.Unavailable,
        "이 운영체제에서는 전역 입력 감지를 지원하지 않습니다.");
}
