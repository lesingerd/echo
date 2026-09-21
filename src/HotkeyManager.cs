namespace EchoTray;

/// <summary>Owns Echo's single system-wide shortcut. WM_HOTKEY arrives at the message window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    public const int HotkeyId = 0x1EC0;

    private readonly IntPtr _hwnd;
    private bool _registered;

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    /// <summary>Returns null on success, or a message explaining why the shortcut is unavailable.</summary>
    public string? Register(string hotkey)
    {
        Unregister();

        if (!HotkeyParser.TryParse(hotkey, out uint modifiers, out uint virtualKey))
        {
            return $"\"{hotkey}\" is not a valid shortcut. Use something like Ctrl+Alt+E.";
        }

        if (!NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            return $"{hotkey} is already in use by another program.";
        }

        _registered = true;
        return null;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    public void Dispose() => Unregister();
}
