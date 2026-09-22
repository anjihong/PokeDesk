using System.Runtime.InteropServices;

namespace DeskPokemon.Platform;

internal sealed class MacInputMonitor : NativeInputMonitor
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint LeftMouseDown = 1, RightMouseDown = 3, KeyDown = 10, FlagsChanged = 12, OtherMouseDown = 25;
    private const uint TapDisabledByTimeout = 0xfffffffe, TapDisabledByUserInput = 0xffffffff;
    private const int KeyboardAutorepeat = 8, MouseButtonNumber = 3;
    private const ulong EventMask = (1UL << (int)LeftMouseDown) | (1UL << (int)RightMouseDown) |
        (1UL << (int)KeyDown) | (1UL << (int)FlagsChanged) | (1UL << (int)OtherMouseDown);
    private readonly EventTapCallback _callback;
    private readonly MacModifierTracker _modifiers = new();
    private nint _tap;
    private bool _tapDisabledByUser;

    public MacInputMonitor() => _callback = Callback;

    public override void RequestPermissionAndRetry()
    {
        if (IsStopping) return;
        // Only an explicit UI action reaches this API; constructing the pet never prompts.
        if (!CGPreflightListenEventAccess()) CGRequestListenEventAccess();
        Retry();
    }

    protected override void RunMonitor()
    {
        if (!CGPreflightListenEventAccess())
        {
            ShowPermissionRequired();
            return;
        }

        nint source = 0, mode = 0;
        nint runLoop = CFRunLoopGetCurrent();
        _tapDisabledByUser = false;
        try
        {
            // kCGSessionEventTap, kCGHeadInsertEventTap, kCGEventTapOptionListenOnly.
            // The tap cannot modify, suppress, or synthesize another application's input.
            _tap = CGEventTapCreate(1, 0, 1, EventMask, _callback, 0);
            if (_tap == 0)
            {
                if (!CGPreflightListenEventAccess()) ShowPermissionRequired();
                else SetStatus(InputHookStatus.Unavailable,
                    "입력 감지를 시작하지 못했습니다. 입력 모니터링 권한을 확인한 뒤 앱을 다시 실행해 주세요.");
                return;
            }

            // Use a CFString owned by this run loop instead of depending on an exported variable address.
            mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
            source = CFMachPortCreateRunLoopSource(0, _tap, 0);
            if (mode == 0 || source == 0) throw new InvalidOperationException("Cannot create input run-loop source.");
            CFRunLoopAddSource(runLoop, source, mode);
            _modifiers.Reset(CGEventSourceFlagsState(0));
            CGEventTapEnable(_tap, true);
            SetStatus(InputHookStatus.Active, "키보드와 마우스 입력을 감지하고 있습니다.");
            while (!IsStopping && !_tapDisabledByUser)
            {
                // The short bounded run permits disposal without touching native objects across threads.
                int runResult = CFRunLoopRunInMode(mode, 0.25, false);
                if (runResult is 1 or 2) // Finished/stopped: the native source no longer services events.
                {
                    SetStatus(InputHookStatus.Unavailable, "입력 감지가 중지되었습니다. 다시 시도해 주세요.");
                    return;
                }
                if (!CGPreflightListenEventAccess())
                {
                    ShowPermissionRequired();
                    return;
                }
            }
            if (_tapDisabledByUser && !IsStopping)
            {
                if (!CGPreflightListenEventAccess()) ShowPermissionRequired();
                else SetStatus(InputHookStatus.Unavailable, "입력 감지가 중지되었습니다. 다시 시도해 주세요.");
            }
        }
        finally
        {
            if (source != 0)
            {
                if (mode != 0) CFRunLoopRemoveSource(runLoop, source, mode);
                CFRelease(source);
            }
            if (_tap != 0)
            {
                CGEventTapEnable(_tap, false);
                CFMachPortInvalidate(_tap);
                CFRelease(_tap);
                _tap = 0;
            }
            if (mode != 0) CFRelease(mode);
        }
    }

    private void ShowPermissionRequired() => SetStatus(InputHookStatus.PermissionRequired,
        "시스템 설정 → 개인정보 보호 및 보안 → 입력 모니터링에서 PokeDesk를 허용한 뒤 다시 시도해 주세요. 앱 재실행이 필요할 수 있습니다.");

    private nint Callback(nint proxy, uint type, nint nativeEvent, nint userInfo)
    {
        if (IsStopping) return nativeEvent;
        if (type == TapDisabledByTimeout)
        {
            if (_tap != 0) CGEventTapEnable(_tap, true);
            return nativeEvent;
        }
        if (type == TapDisabledByUserInput)
        {
            _tapDisabledByUser = true;
            return nativeEvent;
        }
        bool pressed = type switch
        {
            KeyDown => CGEventGetIntegerValueField(nativeEvent, KeyboardAutorepeat) == 0,
            FlagsChanged => _modifiers.FlagsChanged(CGEventGetFlags(nativeEvent)),
            LeftMouseDown or RightMouseDown => true,
            // Match the Windows left/right/middle button behavior; ignore extra side buttons.
            OtherMouseDown => CGEventGetIntegerValueField(nativeEvent, MouseButtonNumber) == 2,
            _ => false
        };
        if (pressed) PublishTrigger();
        return nativeEvent;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EventTapCallback(nint proxy, uint type, nint nativeEvent, nint userInfo);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightListenEventAccess();
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestListenEventAccess();
    [DllImport(CoreGraphics)]
    private static extern nint CGEventTapCreate(uint tap, uint place, uint options, ulong mask, EventTapCallback callback, nint userInfo);
    [DllImport(CoreGraphics)]
    private static extern void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport(CoreGraphics)]
    private static extern long CGEventGetIntegerValueField(nint nativeEvent, int field);
    [DllImport(CoreGraphics)]
    private static extern ulong CGEventGetFlags(nint nativeEvent);
    [DllImport(CoreGraphics)]
    private static extern ulong CGEventSourceFlagsState(int state);
    [DllImport(CoreFoundation)]
    private static extern nint CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)]
    private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)]
    private static extern nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);
    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopAddSource(nint loop, nint source, nint mode);
    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopRemoveSource(nint loop, nint source, nint mode);
    [DllImport(CoreFoundation)]
    private static extern int CFRunLoopRunInMode(nint mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
    [DllImport(CoreFoundation)]
    private static extern void CFMachPortInvalidate(nint port);
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);
}
