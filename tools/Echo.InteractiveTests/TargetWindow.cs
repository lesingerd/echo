namespace EchoTray.InteractiveTests;

/// <summary>
/// Stands in for "the window the user clicks next". It takes the foreground, records whatever
/// gets typed into it, and closes itself so a failed run cannot leave windows behind.
/// </summary>
internal static class TargetWindow
{
    public static void Run(string resultFile)
    {
        using var form = new Form
        {
            Text = "Echo arm target",
            ClientSize = new Size(560, 150),
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
        };

        var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 11) };
        form.Controls.Add(box);

        // Write once the typing has stopped, rather than on every keystroke.
        var settle = new System.Windows.Forms.Timer { Interval = 700 };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try
            {
                File.WriteAllText(resultFile, box.Text);
            }
            catch (Exception)
            {
                // The driver treats a missing file as "nothing arrived".
            }
        };

        box.TextChanged += (_, _) =>
        {
            settle.Stop();
            settle.Start();
        };

        var life = new System.Windows.Forms.Timer { Interval = 25000 };
        life.Tick += (_, _) => form.Close();
        life.Start();

        form.Shown += (_, _) =>
        {
            form.Activate();
            box.Focus();
        };

        Application.Run(form);
    }
}
