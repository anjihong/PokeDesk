using System.Runtime.InteropServices;

namespace DeskPokemon;

/// <summary>
/// 전역 키 다운·마우스 버튼 다운 감지. 키는 눌림 전이마다 한 번만 알리고 자동 반복은 무시한다.
/// 마우스 이동은 무시(WM_MOUSEMOVE 0x200 추가하면 반응).
/// </summary>
public sealed class InputHook : IDisposable
{
    public event Action? Triggered;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    private readonly HookProc _proc; // 필드에 보관해 GC로부터 델리게이트 보호
    private readonly nint _keyboardHook;
    private readonly nint _mouseHook;

    // 저수준 훅의 KBDLLHOOKSTRUCT에는 자동 반복 여부 플래그가 없어 눌린 키를 직접 추적한다.
    // 콜백은 훅을 설치한 스레드에서만 불리므로 잠금 불필요.
    // ponytail: keyup을 놓치면(키 누른 채 화면 전환 등) 그 키의 다음 눌림 한 번이 무시됨. 문제되면 타임아웃 추가.
    private readonly HashSet<int> _pressed = new();

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
                    // KBDLLHOOKSTRUCT.vkCode = 구조체 첫 4바이트. 이미 눌려 있으면 자동 반복이므로 무시.
                    if (_pressed.Add(Marshal.ReadInt32(lParam)))
                        Triggered?.Invoke();
                    break;
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    _pressed.Remove(Marshal.ReadInt32(lParam));
                    break;
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
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
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
