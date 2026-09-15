using System.Runtime.InteropServices;

namespace DeskPokemon;

/// <summary>전역 키 다운·마우스 버튼 다운 감지. 마우스 이동은 무시(WM_MOUSEMOVE 0x200 추가하면 반응).</summary>
public sealed class InputHook : IDisposable
{
    public event Action? Triggered;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    private readonly HookProc _proc; // 필드에 보관해 GC로부터 델리게이트 보호
    private readonly nint _keyboardHook;
    private readonly nint _mouseHook;

    public InputHook()
    {
        _proc = Callback;
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, module, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _proc, module, 0);
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            switch ((int)wParam)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    Triggered?.Invoke();
                    break;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_keyboardHook);
        UnhookWindowsHookEx(_mouseHook);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
