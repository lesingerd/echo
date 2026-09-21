namespace EchoTray;

/// <summary>Plain code-built preferences dialog -- no designer files to keep in sync.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _working;

    private readonly NumericUpDown _charDelay = new() { Minimum = 0, Maximum = 1000, Width = 70 };
    private readonly NumericUpDown _jitter = new() { Minimum = 0, Maximum = 100, Width = 70 };
    private readonly NumericUpDown _settleDelay = new() { Minimum = 0, Maximum = 10000, Increment = 50, Width = 70 };
    private readonly NumericUpDown _armTimeout = new() { Minimum = 5, Maximum = 3600, Width = 70 };
    private readonly NumericUpDown _tabSpaces = new() { Minimum = 1, Maximum = 16, Width = 70 };
    private readonly NumericUpDown _historySize = new() { Minimum = 0, Maximum = 25, Width = 70 };

    private readonly ComboBox _newlines = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _tabs = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    private readonly TextBox _presets = new() { Width = 210 };
    private readonly TextBox _hotkey = new() { Width = 210, ReadOnly = true, TextAlign = HorizontalAlignment.Center };

    private readonly CheckBox _trimTrailing = new() { Text = "Ignore blank lines at the end", AutoSize = true };
    private readonly CheckBox _stopOnChange = new() { Text = "Stop if the active window changes", AutoSize = true };
    private readonly CheckBox _escCancels = new() { Text = "Esc cancels a countdown or stops typing", AutoSize = true };
    private readonly CheckBox _releaseModifiers = new() { Text = "Release held Ctrl/Alt/Shift before typing", AutoSize = true };
    private readonly CheckBox _balloons = new() { Text = "Show notifications", AutoSize = true };
    private readonly CheckBox _hotkeyEnabled = new() { Text = "Global shortcut", AutoSize = true };

    public SettingsForm(AppSettings settings)
    {
        _working = settings;

        Text = "Echo settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;

        try
        {
            if (Environment.ProcessPath is string exe)
            {
                Icon = Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (Exception)
        {
            // An icon is not worth failing the dialog over.
        }

        _newlines.Items.AddRange(new object[] { "Press Enter", "Press Shift+Enter", "Replace with a space", "Skip" });
        _tabs.Items.AddRange(new object[] { "Press Tab", "Replace with spaces", "Skip" });

        TableLayoutPanel layout = BuildLayout();
        Controls.Add(layout);
        LoadFrom(settings);

        // The form has to take its size from the layout, or it opens at the default 300x300
        // and crops everything.
        layout.PerformLayout();
        Size preferred = layout.PreferredSize;
        ClientSize = preferred;
        MinimumSize = new Size(
            preferred.Width + (Size.Width - ClientSize.Width),
            preferred.Height + (Size.Height - ClientSize.Height));
    }

    public AppSettings Result => _working;

    private TableLayoutPanel BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;

        void Section(string title)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, row == 0 ? 0 : 14, 0, 4),
            };

            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 3);
            row++;
        }

        void Row(string label, Control control, string hint = "")
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 10, 6) }, 0, row);
            control.Margin = new Padding(0, 3, 10, 3);
            table.Controls.Add(control, 1, row);

            if (hint.Length > 0)
            {
                table.Controls.Add(new Label
                {
                    Text = hint,
                    AutoSize = true,
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(0, 6, 0, 6),
                }, 2, row);
            }

            row++;
        }

        void CheckRow(CheckBox box)
        {
            box.Margin = new Padding(0, 4, 0, 4);
            table.Controls.Add(box, 0, row);
            table.SetColumnSpan(box, 3);
            row++;
        }

        Section("Typing");
        Row("Delay between characters:", _charDelay, "ms  (0 types as fast as the app allows)");
        Row("Random variation:", _jitter, "%  of that delay");
        Row("Pause before typing starts:", _settleDelay, "ms  (lets the window settle first)");

        Section("Text");
        Row("Line breaks:", _newlines);
        Row("Tabs:", _tabs);
        Row("Spaces per tab:", _tabSpaces, "used when tabs are replaced");
        CheckRow(_trimTrailing);

        Section("Behaviour");
        Row("Stop waiting after:", _armTimeout, "seconds  (when armed but no window is clicked)");
        Row("Delay menu presets:", _presets, "seconds, comma separated");
        Row("Clipboard history:", _historySize, "entries kept in memory  (0 turns it off)");
        CheckRow(_stopOnChange);
        CheckRow(_escCancels);
        CheckRow(_releaseModifiers);
        CheckRow(_balloons);

        Section("Shortcut");
        CheckRow(_hotkeyEnabled);
        Row("Types into the window in front:", _hotkey, "click, then press the keys");

        table.Controls.Add(BuildButtons(), 0, row);
        table.SetColumnSpan(table.GetControlFromPosition(0, row)!, 3);

        _hotkey.KeyDown += OnHotkeyKeyDown;
        _hotkeyEnabled.CheckedChanged += (_, _) => _hotkey.Enabled = _hotkeyEnabled.Checked;
        _tabs.SelectedIndexChanged += (_, _) => _tabSpaces.Enabled = _tabs.SelectedIndex == (int)TabMode.Spaces;

        return table;
    }

    private Control BuildButtons()
    {
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var defaults = new Button { Text = "Restore defaults", AutoSize = true };

        save.Click += (_, _) =>
        {
            if (!TrySaveInto(_working))
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        defaults.Click += (_, _) => LoadFrom(new AppSettings());

        AcceptButton = save;
        CancelButton = cancel;

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        right.Controls.Add(save);
        right.Controls.Add(cancel);

        var bar = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 18, 0, 0),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.Controls.Add(defaults, 0, 0);
        bar.Controls.Add(right, 1, 0);

        return bar;
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys code = e.KeyCode;

        if (code is Keys.Back or Keys.Delete)
        {
            _hotkey.Text = string.Empty;
            return;
        }

        if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
        {
            return;
        }

        // Without a modifier the shortcut would swallow that key everywhere.
        if (e.Modifiers == Keys.None)
        {
            return;
        }

        _hotkey.Text = HotkeyParser.Format(e.KeyData);
    }

    private void LoadFrom(AppSettings settings)
    {
        _charDelay.Value = settings.CharDelayMs;
        _jitter.Value = settings.JitterPercent;
        _settleDelay.Value = settings.SettleDelayMs;
        _armTimeout.Value = settings.ArmTimeoutSeconds;
        _tabSpaces.Value = settings.TabSpaces;
        _historySize.Value = Math.Clamp(settings.HistorySize, 0, 25);

        _newlines.SelectedIndex = (int)settings.Newlines;
        _tabs.SelectedIndex = (int)settings.Tabs;
        _tabSpaces.Enabled = settings.Tabs == TabMode.Spaces;

        _presets.Text = string.Join(", ", settings.DelayPresets);
        _hotkey.Text = settings.Hotkey;
        _hotkeyEnabled.Checked = settings.HotkeyEnabled;
        _hotkey.Enabled = settings.HotkeyEnabled;

        _trimTrailing.Checked = settings.TrimTrailingNewlines;
        _stopOnChange.Checked = settings.StopIfWindowChanges;
        _escCancels.Checked = settings.EscapeCancels;
        _releaseModifiers.Checked = settings.ReleaseModifiers;
        _balloons.Checked = settings.ShowBalloons;
    }

    private bool TrySaveInto(AppSettings settings)
    {
        var presets = new List<int>();
        foreach (string part in _presets.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int value) || value <= 0 || value > 3600)
            {
                Complain($"\"{part}\" is not a delay between 1 and 3600 seconds.");
                return false;
            }

            presets.Add(value);
        }

        if (presets.Count == 0)
        {
            Complain("Enter at least one delay preset, for example: 3, 5, 10, 30");
            return false;
        }

        if (_hotkeyEnabled.Checked && !HotkeyParser.TryParse(_hotkey.Text, out _, out _))
        {
            Complain("Click the shortcut box and press a combination that includes Ctrl, Alt or Shift.");
            return false;
        }

        settings.CharDelayMs = (int)_charDelay.Value;
        settings.JitterPercent = (int)_jitter.Value;
        settings.SettleDelayMs = (int)_settleDelay.Value;
        settings.ArmTimeoutSeconds = (int)_armTimeout.Value;
        settings.TabSpaces = (int)_tabSpaces.Value;
        settings.HistorySize = (int)_historySize.Value;

        settings.Newlines = (NewlineMode)_newlines.SelectedIndex;
        settings.Tabs = (TabMode)_tabs.SelectedIndex;
        settings.DelayPresets = presets;

        settings.Hotkey = _hotkey.Text;
        settings.HotkeyEnabled = _hotkeyEnabled.Checked;

        settings.TrimTrailingNewlines = _trimTrailing.Checked;
        settings.StopIfWindowChanges = _stopOnChange.Checked;
        settings.EscapeCancels = _escCancels.Checked;
        settings.ReleaseModifiers = _releaseModifiers.Checked;
        settings.ShowBalloons = _balloons.Checked;

        return true;
    }

    private void Complain(string message) =>
        MessageBox.Show(this, message, "Echo settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
