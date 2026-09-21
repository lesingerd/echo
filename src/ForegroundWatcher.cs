namespace EchoTray;

/// <summary>
/// Watches for the next window the user activates. The hook is out-of-context, so the callback
/// arrives on the thread that installed it -- which must be the UI thread with its message loop.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private const int ObjidWindow = 0;
    private const int ChildidSelf = 0;

    // The delegate has to outlive the hook, so it is held in a field rather than passed inline.
    private readonly NativeMethods.WinEventProc _callback;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
    }

    public event Action<IntPtr>? ForegroundChanged;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Stop();

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND
            && idObject == ObjidWindow
            && idChild == ChildidSelf
            && hwnd != IntPtr.Zero)
        {
            ForegroundChanged?.Invoke(hwnd);
        }
    }
}
