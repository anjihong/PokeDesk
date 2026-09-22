namespace DeskPokemon.Platform;

/// <summary>Only transient pressed-state IDs are kept; no text, key history, or timestamps are stored.</summary>
internal sealed class PressedKeyTracker
{
    private readonly HashSet<int> _pressed = new();
    public bool KeyDown(int key) => _pressed.Add(key);
    public void KeyUp(int key) => _pressed.Remove(key);
    public void Clear() => _pressed.Clear();
}

internal sealed class MacModifierTracker
{
    // IOLLEvent.h: left/right Shift, Control, Option and Command, plus the Fn flag.
    // Physical bits distinguish pressing both Shift keys. The aggregate bits cover
    // keyboards which do not supply device-specific flags.
    private const ulong DownMask = 0x0000207f | 0x009e0000;
    private const ulong CapsLock = 0x00010000;
    private ulong _flags;

    public void Reset(ulong flags) => _flags = flags;

    public bool FlagsChanged(ulong flags)
    {
        bool pressed = ((flags & ~_flags) & DownMask) != 0 || ((_flags ^ flags) & CapsLock) != 0;
        _flags = flags;
        return pressed;
    }
}
