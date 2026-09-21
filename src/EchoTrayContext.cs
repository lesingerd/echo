using System.ComponentModel;

namespace EchoTray;

/// <summary>
/// The whole app: tray icon, menu, and the small state machine that moves between
/// idle, armed (waiting for the next window), counting down, and typing.
/// </summary>
internal sealed class EchoTrayContext : ApplicationContext
{
    /// <summary>Grace period after arming, long enough to cover a menu closing but not a real click.</summary>
    private const int ArmSettleMs = 600;

    private readonly MessageWindow _window;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ForegroundWatcher _watcher;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HotkeyManager _hotkeys;

    private readonly ClipboardHistory _history = new();

    private readonly ToolStripMenuItem _clipboardItem;
    private readonly ToolStripMenuItem _typeNowItem;
    private readonly ToolStripMenuItem _armItem;
    private readonly ToolStripMenuItem _delayItem;
    private readonly ToolStripMenuItem _historyItem;
    private readonly ToolStripMenuItem _cancelItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripSeparator _cancelSeparator;
    private readonly Font _defaultActionFont;

    private AppSettings _settings;
    private EchoState _state = EchoState.Idle;
    private string _pendingText = string.Empty;
    private int _ticksLeft;

    /// <summary>The last real window the user was in. Clicking a tray menu item hands the
    /// foreground to Echo, so "type now" needs somewhere sensible to fall back to.</summary>
    private IntPtr _lastTarget;

    /// <summary>When the current arm started, and the window focus is expected to bounce back to.</summary>
    private long _armedAt;
    private IntPtr _armedFrom;
    private CancellationTokenSource? _typing;
    private Icon? _currentIcon;
    private IntPtr _currentIconHandle;
    private SettingsForm? _settingsForm;
    private bool _disposed;

    public EchoTrayContext()
    {
        bool firstRun = !File.Exists(AppSettings.FilePath);
        _settings = AppSettings.Load();
        _history.Capacity = _settings.HistorySize;

        _window = new MessageWindow();
        _window.HotkeyPressed += TypeNow;
        _window.ClipboardChanged += OnClipboardChanged;

        _hotkeys = new HotkeyManager(_window.Handle);

        // Runs for the whole session, not just while armed, so Echo always knows which window
        // the user came from.
        _watcher = new ForegroundWatcher();
        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.Start();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;

        _defaultActionFont = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold);

        _clipboardItem = new ToolStripMenuItem("Clipboard: (empty)") { Enabled = false };
        _typeNowItem = new ToolStripMenuItem("Type now", null, (_, _) => TypeNow());
        _armItem = new ToolStripMenuItem("Type into the next window I click", null, (_, _) => ArmForNextWindow())
        {
            Font = _defaultActionFont,
        };
        _delayItem = new ToolStripMenuItem("Type after a delay");
        _historyItem = new ToolStripMenuItem("Recent clipboard");
        _cancelItem = new ToolStripMenuItem("Cancel", null, (_, _) => CancelPending("Cancelled.")) { Visible = false };
        _cancelSeparator = new ToolStripSeparator { Visible = false };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            CheckOnClick = false,
        };

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _clipboardItem,
            new ToolStripSeparator(),
            _typeNowItem,
            _armItem,
            _delayItem,
            new ToolStripSeparator(),
            _historyItem,
            _cancelSeparator,
            _cancelItem,
            new ToolStripSeparator(),
            _startupItem,
            new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("About Echo", null, (_, _) => ShowAbout()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitEcho()),
        });
        _menu.Opening += OnMenuOpening;

        _tray = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += OnTrayMouseClick;

        RebuildDelayMenu();
        RebuildHistoryMenu();
        UpdateTray();

        // Announced even at startup: a silently dead shortcut just looks like Echo is broken.
        ApplyHotkey(announceFailure: true);

        // Seed the fallback target: the watcher only reports changes, so without this Echo has
        // no idea which window the user was in until they switch away and back.
        IntPtr initial = NativeMethods.GetForegroundWindow();
        if (WindowUtil.IsEligibleTarget(initial))
        {
            _lastTarget = initial;
        }

        _history.Add(ClipboardHelper.GetText());
        ConfigureStartup();

        if (firstRun)
        {
            Balloon(
                "Echo is running",
                "Copy some text, click this icon, then click the window to type it into. "
                + "Echo now starts with Windows.",
                force: true);
        }
    }

    /// <summary>
    /// Adds Echo to Windows startup the first time it runs, then leaves the choice alone --
    /// after that it only keeps the registered path pointing at the copy being run.
    /// </summary>
    private void ConfigureStartup()
    {
        if (!_settings.StartupConfigured)
        {
            string? error = StartupRegistration.SetEnabled(true);
            _settings.StartupConfigured = true;
            TrySaveSettings();

            if (error is not null)
            {
                Balloon("Could not add Echo to startup", error, ToolTipIcon.Warning, force: true);
            }

            return;
        }

        if (StartupRegistration.IsEnabled())
        {
            StartupRegistration.RefreshPath();
        }
    }

    // ---- triggers -------------------------------------------------------

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ArmForNextWindow();
        }
    }

    /// <summary>Snapshot the clipboard, then type into whichever window the user clicks next.</summary>
    private void ArmForNextWindow()
    {
        if (IsBusyTyping())
        {
            return;
        }

        if (_state == EchoState.Armed)
        {
            CancelPending("Echo stood down.");
            return;
        }

        if (TryTakeClipboard(out string text))
        {
            ArmWith(text);
        }
    }

    /// <summary>Type into the window that is already in front (the hotkey and menu path).</summary>
    private void TypeNow()
    {
        if (!IsBusyTyping() && TryTakeClipboard(out string text))
        {
            TypeNowWith(text);
        }
    }

    private void StartCountdown(int seconds)
    {
        if (!IsBusyTyping() && TryTakeClipboard(out string text))
        {
            StartCountdownWith(seconds, text);
        }
    }

    private void ArmWith(string text)
    {
        if (IsBusyTyping())
        {
            return;
        }

        ResetPending();
        _pendingText = text;
        _state = EchoState.Armed;
        _armedAt = Environment.TickCount64;
        _armedFrom = ResolveTarget();
        _ticksLeft = _settings.ArmTimeoutSeconds;
        _timer.Start();
        UpdateTray();

        Balloon("Echo is armed", $"Click the window to type into. “{ClipboardHelper.Preview(text)}”");
    }

    private void TypeNowWith(string text)
    {
        if (IsBusyTyping())
        {
            return;
        }

        ResetPending();
        _pendingText = text;
        StartTyping(ResolveTarget());
    }

    private void StartCountdownWith(int seconds, string text)
    {
        if (IsBusyTyping())
        {
            return;
        }

        ResetPending();
        _pendingText = text;
        _state = EchoState.Counting;
        _ticksLeft = seconds;
        _timer.Start();
        UpdateTray();

        Balloon("Echo is counting down", $"Typing in {seconds}s. Click into the target window. Esc cancels.");
    }

    private bool IsBusyTyping()
    {
        if (_state != EchoState.Typing)
        {
            return false;
        }

        Balloon("Echo is busy", "Still typing. Press Esc to stop it.");
        return true;
    }

    /// <summary>The window in front, or the last real one the user was in if Echo itself has focus.</summary>
    private IntPtr ResolveTarget()
    {
        IntPtr current = NativeMethods.GetForegroundWindow();
        return WindowUtil.IsEligibleTarget(current) ? current : _lastTarget;
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        if (!WindowUtil.IsEligibleTarget(hwnd))
        {
            return;
        }

        _lastTarget = hwnd;

        if (_state != EchoState.Armed)
        {
            return;
        }

        // Arming from the tray menu closes the menu, and Windows hands focus straight back to
        // the window that was behind it. That bounce is not a choice of target, so it is
        // ignored -- but only for that one window, and only briefly. A click on anything else
        // is a real choice and fires straight away.
        if (hwnd == _armedFrom && Environment.TickCount64 - _armedAt < ArmSettleMs)
        {
            return;
        }

        _timer.Stop();
        StartTyping(hwnd);
    }

    private void OnClipboardChanged()
    {
        if (_history.Add(ClipboardHelper.GetText()))
        {
            RebuildHistoryMenu();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_settings.EscapeCancels && NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE))
        {
            CancelPending("Cancelled.");
            return;
        }

        _ticksLeft--;

        if (_state == EchoState.Counting)
        {
            if (_ticksLeft <= 0)
            {
                _timer.Stop();
                StartTyping(ResolveTarget());
                return;
            }

            UpdateTray();
        }
        else if (_state == EchoState.Armed)
        {
            if (_ticksLeft <= 0)
            {
                CancelPending("Echo stood down — no window was clicked.");
                return;
            }

            // The armed icon does not change, so only the tooltip needs refreshing.
            _tray.Text = StatusText();
        }
        else
        {
            _timer.Stop();
        }
    }

    // ---- typing ---------------------------------------------------------

    private void StartTyping(IntPtr target)
    {
        _timer.Stop();

        if (!WindowUtil.IsEligibleTarget(target))
        {
            CancelPending("No target window — Echo stayed quiet.");
            return;
        }

        string text = _pendingText;
        string targetName = WindowUtil.Describe(target);
        AppSettings snapshot = _settings.Clone();

        _state = EchoState.Typing;
        UpdateTray();

        _typing = new CancellationTokenSource();
        CancellationToken token = _typing.Token;

        Task.Run(() =>
        {
            TypeResult result;
            string? failure = null;

            try
            {
                // Let the click that selected the window finish landing before keystrokes start.
                if (snapshot.SettleDelayMs > 0)
                {
                    token.WaitHandle.WaitOne(snapshot.SettleDelayMs);
                }

                result = token.IsCancellationRequested
                    ? TypeResult.Cancelled
                    : TypingEngine.Type(text, snapshot, target, token);
            }
            catch (Exception ex)
            {
                // Whatever went wrong, Echo must not stay wedged in the typing state.
                result = TypeResult.Failed;
                failure = ex.Message;
            }

            try
            {
                _window.BeginInvoke(() => OnTypingFinished(result, targetName, text.Length, failure));
            }
            catch (ObjectDisposedException)
            {
                // Echo exited while typing.
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private void OnTypingFinished(TypeResult result, string targetName, int length, string? failure = null)
    {
        _typing?.Dispose();
        _typing = null;
        _pendingText = string.Empty;
        _state = EchoState.Idle;
        UpdateTray();

        switch (result)
        {
            case TypeResult.Completed:
                Balloon("Echo typed it", $"{length:N0} characters into {targetName}.");
                break;

            case TypeResult.Cancelled:
                Balloon("Typing stopped", "Echo stopped part-way through.");
                break;

            case TypeResult.WindowChanged:
                Balloon(
                    "Typing stopped",
                    "The active window changed, so Echo stopped rather than type into the wrong place.",
                    ToolTipIcon.Warning,
                    force: true);
                break;

            case TypeResult.Blocked:
                Balloon(
                    "Windows blocked the keystrokes",
                    "The target window is probably running as administrator. Run Echo as administrator too.",
                    ToolTipIcon.Warning,
                    force: true);
                break;

            case TypeResult.NothingToType:
                Balloon("Nothing to type", "The clipboard text was empty once formatting was applied.");
                break;

            case TypeResult.Failed:
                Balloon("Echo could not type", failure ?? "Something went wrong.", ToolTipIcon.Error, force: true);
                break;
        }
    }

    private void CancelPending(string message)
    {
        _timer.Stop();
        _typing?.Cancel();
        ResetPending();

        if (_state != EchoState.Typing)
        {
            _state = EchoState.Idle;
            UpdateTray();
        }

        Balloon("Echo", message);
    }

    private void ResetPending()
    {
        _pendingText = string.Empty;
        _ticksLeft = 0;
    }

    private bool TryTakeClipboard(out string text)
    {
        text = ClipboardHelper.GetText();

        if (string.IsNullOrEmpty(text))
        {
            Balloon("Nothing to echo", "The clipboard has no text in it.", ToolTipIcon.Warning, force: true);
            return false;
        }

        return true;
    }

    // ---- menu -----------------------------------------------------------

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        string clipboard = ClipboardHelper.GetText();
        bool hasText = clipboard.Length > 0;
        bool busy = _state == EchoState.Typing;

        _clipboardItem.Text = hasText
            ? "Clipboard: " + MenuText(ClipboardHelper.Preview(clipboard))
            : "Clipboard: (no text)";

        _typeNowItem.Enabled = hasText && !busy;
        _delayItem.Enabled = hasText && !busy;
        _historyItem.Enabled = !busy && _history.Entries.Count > 0;
        _historyItem.Text = _history.Entries.Count > 0
            ? $"Recent clipboard ({_history.Entries.Count})"
            : "Recent clipboard";
        _armItem.Enabled = (hasText || _state == EchoState.Armed) && !busy;
        _armItem.Text = _state == EchoState.Armed
            ? "Stop waiting for a window"
            : "Type into the next window I click";

        _typeNowItem.ShortcutKeyDisplayString = _settings.HotkeyEnabled ? _settings.Hotkey : string.Empty;

        bool pending = _state != EchoState.Idle;
        _cancelItem.Visible = pending;
        _cancelSeparator.Visible = pending;

        _startupItem.Checked = StartupRegistration.IsEnabled();
    }

    private void RebuildDelayMenu()
    {
        ClearItems(_delayItem.DropDownItems);

        foreach (int seconds in _settings.DelayPresets)
        {
            int captured = seconds;
            _delayItem.DropDownItems.Add(new ToolStripMenuItem(
                $"{captured} seconds", null, (_, _) => StartCountdown(captured)));
        }

        _delayItem.DropDownItems.Add(new ToolStripSeparator());
        _delayItem.DropDownItems.Add(new ToolStripMenuItem("Custom…", null, (_, _) => AskForDelay()));
    }

    /// <summary>
    /// One entry per remembered clipboard item. Clicking an entry runs the default action;
    /// its submenu offers the other ways to send it.
    /// </summary>
    private void RebuildHistoryMenu()
    {
        ClearItems(_historyItem.DropDownItems);

        IReadOnlyList<string> entries = _history.Entries;
        if (entries.Count == 0)
        {
            _historyItem.DropDownItems.Add(new ToolStripMenuItem(
                _settings.HistorySize == 0 ? "History is turned off" : "Nothing copied yet") { Enabled = false });
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            string text = entries[i];
            var entry = new ToolStripMenuItem($"{i + 1}.  {MenuText(ClipboardHelper.Preview(text))}")
            {
                ToolTipText = $"{text.Length:N0} characters — click to type into the next window you pick",
            };

            // A menu item that owns a submenu does not close the menu by itself, so it is
            // closed here before the default action runs.
            entry.Click += (_, _) =>
            {
                _menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                ArmWith(text);
            };
            entry.DropDownItems.AddRange(BuildEntryActions(text));
            _historyItem.DropDownItems.Add(entry);
        }

        _historyItem.DropDownItems.Add(new ToolStripSeparator());
        _historyItem.DropDownItems.Add(new ToolStripMenuItem("Clear history", null, (_, _) =>
        {
            _history.Clear();
            RebuildHistoryMenu();
        }));
    }

    private ToolStripItem[] BuildEntryActions(string text)
    {
        var arm = new ToolStripMenuItem("Type into the next window I click", null, (_, _) => ArmWith(text))
        {
            Font = _defaultActionFont,
        };

        var now = new ToolStripMenuItem("Type now", null, (_, _) => TypeNowWith(text));

        var delay = new ToolStripMenuItem("Type after a delay");
        foreach (int seconds in _settings.DelayPresets)
        {
            int captured = seconds;
            delay.DropDownItems.Add(new ToolStripMenuItem(
                $"{captured} seconds", null, (_, _) => StartCountdownWith(captured, text)));
        }

        delay.DropDownItems.Add(new ToolStripSeparator());
        delay.DropDownItems.Add(new ToolStripMenuItem("Custom…", null, (_, _) =>
        {
            int seconds = DelayPrompt.Ask(_window, _settings.CustomDelaySeconds);
            if (seconds > 0)
            {
                _settings.CustomDelaySeconds = seconds;
                TrySaveSettings();
                StartCountdownWith(seconds, text);
            }
        }));

        var copy = new ToolStripMenuItem("Put back on the clipboard", null, (_, _) =>
        {
            if (ClipboardHelper.SetText(text))
            {
                Balloon("Echo", $"Copied: “{ClipboardHelper.Preview(text)}”");
            }
            else
            {
                Balloon("Could not copy", "Another program is holding the clipboard.", ToolTipIcon.Warning, force: true);
            }
        });

        return new ToolStripItem[] { arm, now, delay, new ToolStripSeparator(), copy };
    }

    /// <summary>ToolStrip treats a single ampersand as a mnemonic, so previews have to double them.</summary>
    private static string MenuText(string text) => text.Replace("&", "&&", StringComparison.Ordinal);

    private static void ClearItems(ToolStripItemCollection items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            ToolStripItem item = items[i];
            items.RemoveAt(i);
            item.Dispose();
        }
    }

    private void AskForDelay()
    {
        int seconds = DelayPrompt.Ask(_window, _settings.CustomDelaySeconds);
        if (seconds <= 0)
        {
            return;
        }

        _settings.CustomDelaySeconds = seconds;
        TrySaveSettings();
        StartCountdown(seconds);
    }

    private void ToggleStartup()
    {
        bool enable = !StartupRegistration.IsEnabled();
        string? error = StartupRegistration.SetEnabled(enable);

        if (error is not null)
        {
            Balloon("Could not change startup", error, ToolTipIcon.Error, force: true);
            return;
        }

        Balloon("Echo", enable ? "Echo will start with Windows." : "Echo will no longer start with Windows.");
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        var form = new SettingsForm(_settings.Clone());
        _settingsForm = form;

        form.FormClosed += (_, _) =>
        {
            if (form.DialogResult == DialogResult.OK)
            {
                ApplySettings(form.Result);
            }

            _settingsForm = null;
            form.Dispose();
        };

        form.Show();
        form.Activate();
    }

    private void ShowAbout()
    {
        string version = typeof(EchoTrayContext).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        string shortcut = _settings.HotkeyEnabled ? _settings.Hotkey : "(off)";

        MessageBox.Show(
            $"""
             Echo {version}

             Types whatever is on the clipboard into another window as real
             keystrokes, for fields and consoles that will not take a paste.

             Click the tray icon   Arm Echo, then click the window to type into
             Right click           Type now, or type after a delay
             {shortcut,-21} Type into the window in front right now
             Esc                   Cancel a countdown or stop typing

             Settings: {AppSettings.FilePath}
             """,
            "About Echo",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ApplySettings(AppSettings updated)
    {
        updated.Normalize();
        _settings = updated;
        _history.Capacity = _settings.HistorySize;
        TrySaveSettings();
        RebuildDelayMenu();
        RebuildHistoryMenu();
        ApplyHotkey(announceFailure: true);
        UpdateTray();
    }

    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            Balloon("Could not save settings", ex.Message, ToolTipIcon.Error, force: true);
        }
    }

    private void ApplyHotkey(bool announceFailure)
    {
        _hotkeys.Unregister();

        if (!_settings.HotkeyEnabled)
        {
            return;
        }

        string? error = _hotkeys.Register(_settings.Hotkey);
        if (error is not null && announceFailure)
        {
            Balloon("Shortcut not available", error, ToolTipIcon.Warning, force: true);
        }
    }

    // ---- tray presentation ----------------------------------------------

    private void UpdateTray()
    {
        int? seconds = _state == EchoState.Counting ? _ticksLeft : null;
        (Icon icon, IntPtr handle) = TrayIconFactory.Create(_state, seconds);

        Icon? previousIcon = _currentIcon;
        IntPtr previousHandle = _currentIconHandle;

        _tray.Icon = icon;
        _currentIcon = icon;
        _currentIconHandle = handle;

        previousIcon?.Dispose();
        if (previousHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(previousHandle);
        }

        _tray.Text = StatusText();
    }

    private string StatusText()
    {
        string text = _state switch
        {
            EchoState.Armed => "Echo: armed — click the window to type into",
            EchoState.Counting => $"Echo: typing in {_ticksLeft}s (Esc cancels)",
            EchoState.Typing => "Echo: typing…",
            _ => "Echo: click to type the clipboard into the next window",
        };

        // NotifyIcon.Text rejects anything longer than 63 characters.
        return text.Length <= 63 ? text : text[..63];
    }

    private void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
    {
        if (!_settings.ShowBalloons && !force)
        {
            return;
        }

        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(3000);
    }

    // ---- shutdown -------------------------------------------------------

    private void ExitEcho()
    {
        _typing?.Cancel();
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        // WinForms disposes the context when the message loop ends, and Program disposes it
        // again on the way out, so this has to be safe to run twice.
        if (disposing && !_disposed)
        {
            _disposed = true;

            CancellationTokenSource? typing = _typing;
            _typing = null;

            try
            {
                typing?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            // Deliberately not disposed: the typing thread may still be waiting on its token.
            _timer.Stop();
            _timer.Dispose();
            _watcher.Dispose();
            _hotkeys.Dispose();

            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();

            _currentIcon?.Dispose();
            if (_currentIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }

            _settingsForm?.Dispose();
            _defaultActionFont.Dispose();
            _window.Dispose();
        }

        base.Dispose(disposing);
    }
}
