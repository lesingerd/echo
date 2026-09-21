namespace EchoTray;

/// <summary>Small "type in N seconds" prompt for the Custom entry in the delay menu.</summary>
internal static class DelayPrompt
{
    /// <summary>Returns the chosen number of seconds, or 0 if the user backed out.</summary>
    public static int Ask(IWin32Window owner, int initialSeconds)
    {
        using var form = new Form
        {
            Text = "Type after a delay",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Font,
            TopMost = true,
        };

        var seconds = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 3600,
            Value = Math.Clamp(initialSeconds, 1, 3600),
            Width = 80,
            Margin = new Padding(0, 3, 8, 3),
        };

        var ok = new Button { Text = "Start", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
        };

        layout.Controls.Add(new Label { Text = "Wait", AutoSize = true, Margin = new Padding(0, 6, 8, 6) }, 0, 0);
        layout.Controls.Add(seconds, 1, 0);
        layout.Controls.Add(new Label { Text = "seconds, then type", AutoSize = true, Margin = new Padding(0, 6, 0, 6) }, 2, 0);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        layout.Controls.Add(buttons, 0, 1);
        layout.SetColumnSpan(buttons, 3);

        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog(owner) == DialogResult.OK ? (int)seconds.Value : 0;
    }
}
