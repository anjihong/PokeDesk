using System.Diagnostics;

namespace DeskPokemon.Platform;

internal sealed class MacInputMonitor : NativeInputMonitor
{
    private readonly IMacInputBackend _backend;
    private readonly TimeSpan _retryDelay;
    private readonly MacModifierTracker _modifiers = new();
    private IMacInputTap? _keyboardTap, _buttonsTap;
    private bool _tapStopped;
    private int _session;

    public MacInputMonitor() : this(new MacInputBackend()) { }

    internal MacInputMonitor(IMacInputBackend backend, TimeSpan? retryDelay = null)
    {
        _backend = backend;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        if (_retryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));
    }

    protected override TimeSpan AutomaticRetryDelay => _retryDelay;

    public override void RequestPermissionAndRetry()
    {
        if (IsStopping) return;
        // Only an explicit UI action reaches the prompting API. Either nonprompting
        // query can authorize a tap attempt when macOS's permission APIs disagree.
        if (!_backend.ReadPermission().CanListen) _backend.RequestListenPermission();
        Retry();
    }

    protected override void RunMonitor()
    {
        if (!_backend.ReadPermission().CanListen)
        {
            ShowPermissionRequired();
            return;
        }

        IMacInputTap? keyboard = null, buttons = null;
        var session = Interlocked.Increment(ref _session);
        _tapStopped = false;
        try
        {
            keyboard = _backend.CreateListenOnlyTap(MacInputTapKind.Keyboard,
                input => OnInput(session, MacInputTapKind.Keyboard, input));
            _keyboardTap = keyboard;
            if (IsStopping) return;
            if (keyboard is null || !keyboard.IsValid)
            {
                ReportFailedConnection();
                return;
            }
            buttons = _backend.CreateListenOnlyTap(MacInputTapKind.ButtonsAndModifiers,
                input => OnInput(session, MacInputTapKind.ButtonsAndModifiers, input));
            _buttonsTap = buttons;
            if (IsStopping) return;
            if (buttons is null || !buttons.IsValid)
            {
                ReportFailedConnection();
                return;
            }
            _modifiers.Reset(keyboard.ModifierFlags);
            keyboard.Enable();
            buttons.Enable();
            if (!IsHealthy(keyboard) || !IsHealthy(buttons))
            {
                ReportFailedConnection();
                return;
            }

            SetStatus(InputHookStatus.Active, "키보드와 마우스 입력을 감지하고 있습니다.");
            while (!IsStopping)
            {
                // Both sources were installed on this worker's default run loop.
                var result = keyboard.RunSlice();
                if (IsStopping) return;
                if (_tapStopped || result is MacRunLoopResult.Finished or MacRunLoopResult.Stopped ||
                    !IsHealthy(keyboard) || !IsHealthy(buttons))
                {
                    ReportFailedConnection();
                    return;
                }
                // A false/stale permission query cannot invalidate a live, enabled tap.
                // Query permissions again only when the actual native connection fails.
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"macOS input connection failed: {ex}");
            if (!IsStopping) ReportFailedConnection();
        }
        finally
        {
            Interlocked.Increment(ref _session);
            _keyboardTap = _buttonsTap = null;
            try { buttons?.Dispose(); }
            finally { keyboard?.Dispose(); }
        }
    }

    private static bool IsHealthy(IMacInputTap tap) => tap.IsValid && tap.IsEnabled;

    private void ReportFailedConnection()
    {
        if (!_backend.ReadPermission().CanListen) ShowPermissionRequired();
        else SetStatus(InputHookStatus.Unavailable,
            "입력 모니터링 권한은 있지만 입력 감지 연결이 작동하지 않습니다. 현재 실행 중인 DeskPokemon 사본의 권한 등록을 확인하고 앱을 완전히 종료한 뒤 다시 실행해 주세요.");
    }

    private void ShowPermissionRequired() => SetStatus(InputHookStatus.PermissionRequired,
        "시스템 설정 → 개인정보 보호 및 보안 → 입력 모니터링에서 현재 실행 중인 DeskPokemon을 허용해 주세요. 이미 켜져 있다면 현재 앱 사본의 등록을 확인하고 앱을 완전히 종료한 뒤 다시 실행해 주세요.");

    private void OnInput(int session, MacInputTapKind kind, MacInputEvent input)
    {
        var tap = kind == MacInputTapKind.Keyboard ? _keyboardTap : _buttonsTap;
        if (IsStopping || session != Volatile.Read(ref _session) || tap is null || _tapStopped) return;
        if (input.Type == MacInputEventType.TapDisabledByTimeout)
        {
            tap.Enable();
            _tapStopped = !IsHealthy(tap);
            return;
        }
        if (input.Type == MacInputEventType.TapDisabledByUserInput)
        {
            _tapStopped = true;
            return;
        }
        var pressed = input.Type switch
        {
            MacInputEventType.KeyDown => !input.IsKeyboardRepeat,
            MacInputEventType.FlagsChanged => _modifiers.FlagsChanged(input.ModifierFlags),
            MacInputEventType.LeftMouseDown or MacInputEventType.RightMouseDown => true,
            MacInputEventType.OtherMouseDown => input.MouseButton == 2,
            _ => false
        };
        if (pressed) PublishTrigger();
    }
}
