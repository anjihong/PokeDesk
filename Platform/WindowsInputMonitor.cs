using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskPokemon.Platform;

internal sealed class WindowsInputMonitor : NativeInputMonitor
{
    private const int WhKeyboardLl = 13, WhMouseLl = 14;
    private const uint WmQuit = 0x0012;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint WmLeftButtonDown = 0x0201, WmRightButtonDown = 0x0204, WmMiddleButtonDown = 0x0207;
    private readonly HookProc _callback;
    private readonly PressedKeyTracker _pressed = new();
    private volatile uint _threadId;

    public WindowsInputMonitor() => _callback = Callback;

    protected override void RunMonitor()
    {
        // Create the thread's message queue before publishing its ID, so Dispose can post WM_QUIT.
        PeekMessage(out _, 0, 0, 0, 0);
        _threadId = GetCurrentThreadId();
        nint keyboard = 0, mouse = 0;
        try
        {
            if (IsStopping) return;
            nint module = GetModuleHandle(null);
            keyboard = SetWindowsHookEx(WhKeyboardLl, _callback, module, 0);
            if (keyboard == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            mouse = SetWindowsHookEx(WhMouseLl, _callback, module, 0);
            if (mouse == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            SetStatus(InputHookStatus.Active, "키보드와 마우스 입력을 감지하고 있습니다.");
            while (!IsStopping)
            {
                int result = GetMessage(out var message, 0, 0, 0);
                if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (result == 0) break;
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            _threadId = 0;
            _pressed.Clear();
        }
    }

    protected override void WakeNativeLoop()
    {
        uint threadId = _threadId;
        if (threadId != 0) PostThreadMessage(threadId, WmQuit, 0, 0);
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !IsStopping)
        {
            switch ((uint)wParam)
            {
                case WmKeyDown:
                case WmSysKeyDown:
                    if (_pressed.KeyDown(Marshal.ReadInt32(lParam))) PublishTrigger();
                    break;
                case WmKeyUp:
                case WmSysKeyUp:
                    _pressed.KeyUp(Marshal.ReadInt32(lParam));
                    break;
                case WmLeftButtonDown:
                case WmRightButtonDown:
                case WmMiddleButtonDown:
                    PublishTrigger();
                    break;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}
