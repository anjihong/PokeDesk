using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskPokemon.Platform;

internal enum MacListenAccess { Granted = 0, Denied = 1, Unknown = 2 }
internal readonly record struct MacInputPermission(bool CoreGraphicsGranted, MacListenAccess HidAccess)
{
    public bool CanListen => CoreGraphicsGranted || HidAccess == MacListenAccess.Granted;
}

internal enum MacInputEventType : uint
{
    LeftMouseDown = 1, RightMouseDown = 3, KeyDown = 10, FlagsChanged = 12, OtherMouseDown = 25,
    TapDisabledByTimeout = 0xfffffffe, TapDisabledByUserInput = 0xffffffff
}

// No key codes, text, cursor positions, or application identities leave the native callback.
internal readonly record struct MacInputEvent(MacInputEventType Type, bool IsKeyboardRepeat = false,
    ulong ModifierFlags = 0, int MouseButton = 0);
internal enum MacRunLoopResult { Finished = 1, Stopped = 2, TimedOut = 3, HandledSource = 4 }
internal enum MacInputTapKind { Keyboard, ButtonsAndModifiers }

internal interface IMacInputBackend
{
    MacInputPermission ReadPermission();
    void RequestListenPermission();
    IMacInputTap? CreateListenOnlyTap(MacInputTapKind kind, Action<MacInputEvent> onInput);
}

internal interface IMacInputTap : IDisposable
{
    bool IsValid { get; }
    bool IsEnabled { get; }
    ulong ModifierFlags { get; }
    void Enable();
    MacRunLoopResult RunSlice();
}

/// <summary>Passive CoreGraphics event tap and its CF objects, owned by one worker thread.</summary>
internal sealed class MacInputBackend : IMacInputBackend
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const ulong ButtonAndModifierMask = (1UL << (int)MacInputEventType.LeftMouseDown) |
        (1UL << (int)MacInputEventType.RightMouseDown) |
        (1UL << (int)MacInputEventType.FlagsChanged) | (1UL << (int)MacInputEventType.OtherMouseDown);

    public MacInputPermission ReadPermission() => new(CGPreflightListenEventAccess(), IOHIDCheckAccess(1));
    public void RequestListenPermission() => IOHIDRequestAccess(1);
    public IMacInputTap? CreateListenOnlyTap(MacInputTapKind kind, Action<MacInputEvent> onInput) =>
        NativeTap.Create(kind, onInput);

    private sealed class NativeTap : IMacInputTap
    {
        private readonly Action<MacInputEvent> _onInput;
        private readonly EventTapCallback _callback;
        private nint _tap, _source, _mode, _runLoop;
        private bool _sourceAttached;

        private NativeTap(Action<MacInputEvent> onInput)
        {
            _onInput = onInput;
            _callback = Callback;
        }

        public static NativeTap? Create(MacInputTapKind kind, Action<MacInputEvent> onInput)
        {
            var connection = new NativeTap(onInput);
            try
            {
                connection._runLoop = CFRunLoopGetCurrent();
                // CoreGraphics may strip forbidden key bits from a mixed mask and still
                // return a mouse-only tap. A separate KeyDown-only tap cannot silently
                // succeed without keyboard access; FlagsChanged belongs with buttons.
                var mask = kind == MacInputTapKind.Keyboard ? 1UL << (int)MacInputEventType.KeyDown : ButtonAndModifierMask;
                // Session location + listen-only: never modify, suppress, or synthesize input.
                connection._tap = CGEventTapCreate(1, 0, 1, mask, connection._callback, 0);
                if (connection._tap == 0 || !CFMachPortIsValid(connection._tap))
                {
                    connection.Dispose();
                    return null;
                }
                connection._mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
                connection._source = CFMachPortCreateRunLoopSource(0, connection._tap, 0);
                if (connection._mode == 0 || connection._source == 0)
                    throw new InvalidOperationException("Cannot create input run-loop source.");
                CFRunLoopAddSource(connection._runLoop, connection._source, connection._mode);
                connection._sourceAttached = true;
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public bool IsValid => _tap != 0 && CFMachPortIsValid(_tap);
        public bool IsEnabled => _tap != 0 && CGEventTapIsEnabled(_tap);
        public ulong ModifierFlags => CGEventSourceFlagsState(0);
        public void Enable()
        {
            if (IsValid) CGEventTapEnable(_tap, true);
        }
        public MacRunLoopResult RunSlice() => (MacRunLoopResult)CFRunLoopRunInMode(_mode, .25, false);

        private nint Callback(nint proxy, uint type, nint nativeEvent, nint userInfo)
        {
            try
            {
                var kind = (MacInputEventType)type;
                _onInput(new MacInputEvent(kind,
                    kind == MacInputEventType.KeyDown && CGEventGetIntegerValueField(nativeEvent, 8) != 0,
                    kind == MacInputEventType.FlagsChanged ? CGEventGetFlags(nativeEvent) : 0,
                    kind == MacInputEventType.OtherMouseDown ? (int)CGEventGetIntegerValueField(nativeEvent, 3) : 0));
            }
            catch (Exception ex) { Debug.WriteLine($"macOS input callback failed: {ex}"); }
            return nativeEvent;
        }

        public void Dispose()
        {
            if (_source != 0)
            {
                if (_sourceAttached) CFRunLoopRemoveSource(_runLoop, _source, _mode);
                CFRelease(_source);
                _source = 0;
                _sourceAttached = false;
            }
            if (_tap != 0)
            {
                CGEventTapEnable(_tap, false);
                CFMachPortInvalidate(_tap);
                CFRelease(_tap);
                _tap = 0;
            }
            if (_mode != 0)
            {
                CFRelease(_mode);
                _mode = 0;
            }
            GC.KeepAlive(_callback);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EventTapCallback(nint proxy, uint type, nint nativeEvent, nint userInfo);
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightListenEventAccess();
    [DllImport(IOKit)]
    private static extern MacListenAccess IOHIDCheckAccess(int requestType);
    [DllImport(IOKit)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool IOHIDRequestAccess(int requestType);
    [DllImport(CoreGraphics)]
    private static extern nint CGEventTapCreate(uint tap, uint place, uint options, ulong mask, EventTapCallback callback, nint userInfo);
    [DllImport(CoreGraphics)]
    private static extern void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventTapIsEnabled(nint tap);
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
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFMachPortIsValid(nint port);
    [DllImport(CoreFoundation)]
    private static extern void CFMachPortInvalidate(nint port);
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);
}
