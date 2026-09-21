namespace EchoTray;

/// <summary>
/// An invisible window that never shows. It gives Echo a message loop anchor for WM_HOTKEY
/// and a handle to marshal worker-thread callbacks back onto the UI thread.
/// </summary>
internal sealed class MessageWindow : Form
{
    public MessageWindow()
    {
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        ClientSize = new Size(1, 1);
        Text = "Echo";

        _ = Handle; // Realise the handle now so Invoke works before anything is shown.

        _listeningToClipboard = NativeMethods.AddClipboardFormatListener(Handle);
    }

    public event Action? HotkeyPressed;

    /// <summary>Raised on the UI thread whenever anything changes the clipboard.</summary>
    public event Action? ClipboardChanged;

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyManager.HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return;
        }

        if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke();
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _listeningToClipboard && !IsDisposed)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            _listeningToClipboard = false;
        }

        base.Dispose(disposing);
    }

    private bool _listeningToClipboard;
}
